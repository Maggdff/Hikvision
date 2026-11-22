package com.voiceup.app.data.voice

import android.content.Context
import com.voiceup.app.domain.voice.VoiceEngine
import com.voiceup.app.domain.voice.VoiceState
import io.livekit.android.LiveKit
import io.livekit.android.events.RoomEvent
import io.livekit.android.room.Room
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/**
 * التنفيذ الوحيد الذي يلمس LiveKit SDK (الإصدار 2.12.3).
 * ملاحظة: كُتب من التوثيق والذاكرة ولم يُترجَم بعد؛ إن اختلف توقيع في الـ SDK فالتعديل محصور هنا.
 */
class LiveKitVoiceEngine(context: Context) : VoiceEngine {

    private val appContext = context.applicationContext
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)

    private val _state = MutableStateFlow(VoiceState.Disconnected)
    override val state: StateFlow<VoiceState> = _state.asStateFlow()

    private val _speaking = MutableStateFlow<Set<String>>(emptySet())
    override val speaking: StateFlow<Set<String>> = _speaking.asStateFlow()

    private var room: Room? = null
    private var eventsJob: Job? = null

    override suspend fun connect(url: String, token: String) {
        disconnect()
        _state.value = VoiceState.Connecting
        val r = LiveKit.create(appContext)
        room = r

        // نبدأ الاستماع للأحداث قبل الاتصال حتى لا يفوتنا حدث
        eventsJob = scope.launch(start = CoroutineStart.UNDISPATCHED) {
            r.events.events.collect { event ->
                when (event) {
                    is RoomEvent.ActiveSpeakersChanged ->
                        _speaking.value = event.speakers.mapNotNull { it.identity?.value }.toSet()
                    is RoomEvent.Reconnecting -> _state.value = VoiceState.Reconnecting
                    is RoomEvent.Reconnected -> _state.value = VoiceState.Connected
                    is RoomEvent.Disconnected -> {
                        _speaking.value = emptySet()
                        _state.value = VoiceState.Disconnected
                    }
                    else -> Unit
                }
            }
        }

        try {
            // connect قد يرمي استثناءً أو يُرجع Result حسب إصدار الـ SDK؛ نعالج الحالتين
            val outcome: Any? = r.connect(url, token)
            if (outcome is Result<*>) outcome.getOrThrow()
            _state.value = VoiceState.Connected
        } catch (e: Exception) {
            _state.value = VoiceState.Failed
            release()
            throw e
        }
    }

    override suspend fun setMicEnabled(enabled: Boolean) {
        val p = room?.localParticipant ?: error("not connected")
        p.setMicrophoneEnabled(enabled)
    }

    override fun disconnect() {
        release()
        _speaking.value = emptySet()
        _state.value = VoiceState.Disconnected
    }

    private fun release() {
        eventsJob?.cancel()
        eventsJob = null
        room?.let {
            it.disconnect()
            it.release()
        }
        room = null
    }
}
