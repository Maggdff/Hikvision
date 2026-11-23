package com.voiceup.app.presentation.room

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.voiceup.app.domain.model.ApiException
import com.voiceup.app.domain.model.ChatMessage
import com.voiceup.app.domain.model.JoinInfo
import com.voiceup.app.domain.model.RoomState
import com.voiceup.app.domain.model.SeatRequest
import com.voiceup.app.domain.repository.ProfileRepository
import com.voiceup.app.domain.repository.RoomsRepository
import com.voiceup.app.domain.voice.VoiceEngine
import com.voiceup.app.domain.voice.VoiceState
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/** سبب انتهاء الجلسة داخل الغرفة؛ الواجهة تعرضه ثم تعود للرئيسية. */
enum class RoomEnd { Closed, Banned, LoadFailed, WrongPassword, TooManyAttempts }

data class RoomUiState(
    val loading: Boolean = true,
    val room: RoomState? = null,
    val names: Map<String, String> = emptyMap(),
    val requests: List<SeatRequest> = emptyList(),
    val messages: List<ChatMessage> = emptyList(),
    val sending: Boolean = false,
    val voice: VoiceState = VoiceState.Disconnected,
    val speaking: Set<String> = emptySet(),
    val micOn: Boolean = false,
    val requestPending: Boolean = false,
    val busy: Boolean = false,
    /** خطأ عابر من إجراء؛ تعرضه الواجهة مرة ثم تمسحه. */
    val errorCode: String? = null,
    val end: RoomEnd? = null,
)

class RoomViewModel(
    private val roomId: String,
    private val rooms: RoomsRepository,
    private val profiles: ProfileRepository,
    private val voice: VoiceEngine,
    private val pollMillis: Long = 4_000,
    private val chatPollMillis: Long = 2_000,
    /** كلمة سر الغرفة الخاصة (6 أرقام)، تبقى في الذاكرة فقط. */
    private val password: String? = null,
) : ViewModel() {

    private val _state = MutableStateFlow(RoomUiState())
    val state: StateFlow<RoomUiState> = _state.asStateFlow()

    private var pollJob: Job? = null
    private var chatJob: Job? = null
    private var preJoin: JoinInfo? = null
    private var lastMessageId: Long? = null
    private var seatKnown = false
    private var lastSeat: Int? = null
    private var left = false

    init {
        viewModelScope.launch { voice.state.collect { v -> _state.update { it.copy(voice = v) } } }
        viewModelScope.launch { voice.speaking.collect { s -> _state.update { it.copy(speaking = s) } } }
        viewModelScope.launch { enter() }
    }

    // ---------- الدخول والاستطلاع ----------

    private suspend fun enter() {
        try {
            // الغرفة الخاصة: الدخول بكلمة السر أولاً، فالخادم لا يكشف حالتها قبل ذلك
            if (password != null) preJoin = rooms.join(roomId, password)
            applyRoom(rooms.getRoomState(roomId))
        } catch (e: ApiException) {
            endFor(e)
            return
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            _state.update { it.copy(loading = false, end = RoomEnd.LoadFailed) }
            return
        }
        _state.update { it.copy(loading = false) }
        connectVoice()
        startPolling()
        startChat()
    }

    private suspend fun connectVoice() {
        try {
            val join = preJoin?.also { preJoin = null } ?: rooms.join(roomId, password)
            voice.connect(join.url, join.token)
            _state.update { if (it.errorCode == "voice_failed") it.copy(errorCode = null) else it }
        } catch (e: ApiException) {
            if (!endFor(e)) _state.update { it.copy(errorCode = e.code) }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            _state.update { it.copy(errorCode = "voice_failed") }
        }
    }

    private fun startPolling() {
        pollJob?.cancel()
        pollJob = viewModelScope.launch {
            while (true) {
                delay(pollMillis)
                refreshOnce()
            }
        }
    }

    /** يحدّث حالة الغرفة وطلبات المقاعد. أخطاء الشبكة العابرة تُتجاهل حتى المحاولة التالية. */
    suspend fun refreshOnce() {
        try {
            applyRoom(rooms.getRoomState(roomId))
        } catch (e: ApiException) {
            endFor(e)
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            // تجاهل
        }
    }

    /** يُرجع true إن كان الخطأ ينهي الجلسة. */
    private fun endFor(e: ApiException): Boolean {
        val end = when {
            e.status == 404 && e.code == "room_not_found" -> RoomEnd.Closed
            e.status == 403 && e.code == "banned_from_room" -> RoomEnd.Banned
            e.status == 403 && e.code == "wrong_password" -> RoomEnd.WrongPassword
            e.status == 429 && e.code == "too_many_attempts" -> RoomEnd.TooManyAttempts
            else -> null
        }
        if (end != null) {
            pollJob?.cancel()
            chatJob?.cancel()
            voice.disconnect()
            _state.update { it.copy(loading = false, end = end) }
            return true
        }
        if (_state.value.room == null) _state.update { it.copy(loading = false, end = RoomEnd.LoadFailed) }
        return false
    }

    private suspend fun applyRoom(room: RoomState) {
        val requests = if (room.myRole.isStaff) {
            try { rooms.listSeatRequests(roomId) } catch (e: ApiException) { _state.value.requests }
        } else emptyList()

        val wanted = (room.seats.map { it.uid } + requests.map { it.uid }).toSet()
        val known = _state.value.names
        val resolved = known.toMutableMap()
        for (uid in wanted - known.keys) {
            try {
                profiles.getProfile(uid)?.let { resolved[uid] = it.displayName.ifBlank { it.username } }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                // الاسم يظهر مختصراً حتى ينجح الجلب لاحقاً
            }
        }

        val previous = lastSeat
        val hadKnown = seatKnown
        lastSeat = room.mySeat
        seatKnown = true

        _state.update {
            it.copy(
                room = room,
                requests = requests,
                names = resolved,
                requestPending = if (room.mySeat != null) false else it.requestPending,
            )
        }

        if (hadKnown && previous == null && room.mySeat != null) {
            // حصلنا على مقعد: نعيد الاتصال برمز جديد يحمل صلاحية الميكروفون (أضمن من الاعتماد على تحديث الصلاحية)
            connectVoice()
        } else if (hadKnown && previous != null && room.mySeat == null) {
            runCatching { voice.setMicEnabled(false) }
            _state.update { it.copy(micOn = false) }
        }
    }

    // ---------- الدردشة ----------

    private fun startChat() {
        chatJob?.cancel()
        chatJob = viewModelScope.launch {
            while (true) {
                refreshMessages()
                delay(chatPollMillis)
            }
        }
    }

    /** يجلب الجديد من الرسائل. الأخطاء العابرة تُتجاهل، وإغلاق الغرفة/الحظر ينهيان الجلسة. */
    suspend fun refreshMessages() {
        try {
            val fresh = rooms.listMessages(roomId, lastMessageId)
            if (fresh.isNotEmpty()) mergeMessages(fresh)
        } catch (e: ApiException) {
            endFor(e)
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            // تجاهل
        }
    }

    private suspend fun mergeMessages(fresh: List<ChatMessage>) {
        val unknown = fresh.map { it.uid }.toSet() - _state.value.names.keys
        val resolved = _state.value.names.toMutableMap()
        for (uid in unknown) {
            try {
                profiles.getProfile(uid)?.let { resolved[uid] = it.displayName.ifBlank { it.username } }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                // يظهر الاسم مختصراً حتى ينجح الجلب
            }
        }
        _state.update { s ->
            val merged = (s.messages + fresh).distinctBy { it.id }.sortedBy { it.id }.takeLast(MAX_MESSAGES)
            lastMessageId = merged.last().id
            s.copy(messages = merged, names = s.names + resolved)
        }
    }

    /** يرسل رسالة؛ عند النجاح يُستدعى onSent (لتفريغ حقل الكتابة). عند الفشل يبقى النص لدى المستخدم. */
    fun sendMessage(text: String, onSent: () -> Unit) {
        val t = text.trim()
        if (t.isEmpty() || t.length > MAX_MESSAGE_LENGTH || _state.value.sending) return
        _state.update { it.copy(sending = true, errorCode = null) }
        viewModelScope.launch {
            try {
                val m = rooms.sendMessage(roomId, t)
                mergeMessages(listOf(m))
                onSent()
            } catch (e: ApiException) {
                if (!endFor(e)) _state.update { it.copy(errorCode = e.code) }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                _state.update { it.copy(errorCode = "unknown") }
            } finally {
                _state.update { it.copy(sending = false) }
            }
        }
    }

    // ---------- إجراءات المستخدم ----------

    private fun act(block: suspend () -> Unit) {
        if (_state.value.busy) return
        _state.update { it.copy(busy = true, errorCode = null) }
        viewModelScope.launch {
            try {
                block()
            } catch (e: ApiException) {
                if (!endFor(e)) _state.update { it.copy(errorCode = e.code) }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                _state.update { it.copy(errorCode = "unknown") }
            } finally {
                _state.update { it.copy(busy = false) }
            }
            if (_state.value.end == null) refreshOnce()
        }
    }

    fun setMic(enabled: Boolean) {
        viewModelScope.launch {
            try {
                voice.setMicEnabled(enabled)
                _state.update { it.copy(micOn = enabled) }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                _state.update { it.copy(micOn = false, errorCode = "mic_failed") }
            }
        }
    }

    fun requestSeat() = act {
        rooms.requestSeat(roomId)
        _state.update { it.copy(requestPending = true) }
    }

    fun leaveSeat() = act {
        rooms.leaveSeat(roomId)
        runCatching { voice.setMicEnabled(false) }
        _state.update { it.copy(micOn = false) }
    }

    fun decide(requestId: String, approve: Boolean) = act { rooms.decideSeatRequest(roomId, requestId, approve) }

    fun mute(uid: String) = act { rooms.mute(roomId, uid) }

    fun kick(uid: String, ban: Boolean) = act { rooms.kick(roomId, uid, ban) }

    fun closeRoom() = act {
        rooms.closeRoom(roomId)
        pollJob?.cancel()
        chatJob?.cancel()
        voice.disconnect()
        _state.update { it.copy(end = RoomEnd.Closed) }
    }

    fun reconnect() {
        viewModelScope.launch { connectVoice() }
    }

    fun clearError() = _state.update { it.copy(errorCode = null) }

    /**
     * زر المغادرة: يحرر المقعد (إن كان المستخدم جالساً وليس المالك) ثم يقطع الصوت.
     * المالك يغادر دون تحرير مقعده؛ عليه إغلاق الغرفة إن أراد إنهاءها.
     */
    fun leaveRoom() {
        val room = _state.value.room
        val mustFreeSeat = room != null && room.mySeat != null && room.myRole != com.voiceup.app.domain.model.RoomRole.Owner
        pollJob?.cancel()
        chatJob?.cancel()
        voice.disconnect()
        left = true
        if (mustFreeSeat) {
            viewModelScope.launch { runCatching { rooms.leaveSeat(roomId) } }
        }
    }

    /** يقطع الصوت ويوقف الاستطلاع فقط (يُستدعى عند تدمير الـ ViewModel). */
    fun leave() {
        if (left) return
        left = true
        pollJob?.cancel()
        chatJob?.cancel()
        voice.disconnect()
    }

    override fun onCleared() {
        leave()
        super.onCleared()
    }

    private companion object {
        const val MAX_MESSAGES = 200
        const val MAX_MESSAGE_LENGTH = 300
    }
}
