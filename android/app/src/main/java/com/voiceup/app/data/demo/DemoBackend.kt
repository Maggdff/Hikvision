package com.voiceup.app.data.demo

import com.voiceup.app.domain.model.ApiException
import com.voiceup.app.domain.model.AuthUser
import com.voiceup.app.domain.model.JoinInfo
import com.voiceup.app.domain.model.ProfileDraft
import com.voiceup.app.domain.model.RoomRole
import com.voiceup.app.domain.model.RoomState
import com.voiceup.app.domain.model.SeatInfo
import com.voiceup.app.domain.model.SeatRequest
import com.voiceup.app.domain.model.UserProfile
import com.voiceup.app.domain.model.UsernameTakenException
import com.voiceup.app.domain.model.VoiceRoom
import com.voiceup.app.domain.repository.AuthRepository
import com.voiceup.app.domain.repository.ProfileRepository
import com.voiceup.app.domain.repository.RoomsRepository
import com.voiceup.app.domain.voice.VoiceEngine
import com.voiceup.app.domain.voice.VoiceState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlin.random.Random

/**
 * وضع تجريبي (Demo): كل شيء في الذاكرة، بلا Firebase ولا خادم ولا LiveKit.
 * يُفعَّل عند البناء بـ VOICEUP_DEMO=true، والغرض منه معاينة الواجهات فقط.
 */
object DemoData {
    const val ME = "demo-me"

    val people = listOf(
        UserProfile("u1", "abu_salman", "أبو سلمان", "شاعر وهاوي أدب"),
        UserProfile("u2", "layla_k", "ليلى", "أحب الكتب والقهوة"),
        UserProfile("u3", "omar_dev", "عمر المطوّر", "مبرمج ومهتم بالذكاء الاصطناعي"),
        UserProfile("u4", "noor_88", "نور", "أتعلم الإنجليزية"),
        UserProfile("u5", "fahad_g", "فهد", "ألعاب وضحك"),
        UserProfile("u6", "sara_m", "سارة", "مصممة"),
        UserProfile("u7", "khalid_r", "خالد", "رياضة وسفر"),
        UserProfile("u8", "mariam", "مريم", "موسيقى"),
    )

    val existingMe = UserProfile(ME, "demo_user", "مستخدم تجريبي", "أجرّب تطبيق VoiceUp")
}

class DemoRoom(
    val id: String,
    val title: String,
    val topic: String?,
    val ownerUid: String,
    val maxSeats: Int,
) {
    val seats = linkedMapOf<Int, SeatInfo>()
    val requests = mutableListOf<SeatRequest>()
    val banned = mutableSetOf<String>()
    var open = true

    fun freeSeat(): Int? = (0 until maxSeats).firstOrNull { it !in seats }
}

class DemoWorld {
    val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    val profiles = mutableMapOf<String, UserProfile>()
    val rooms = linkedMapOf<String, DemoRoom>()
    var counter = 0

    init {
        DemoData.people.forEach { profiles[it.uid] = it }

        seed("شاعر وليل", "قصائد ونقاش أدبي", "u1", 8, listOf("u2" to RoomRole.Moderator, "u3" to RoomRole.Member))
        seed("الذكاء الاصطناعي بالعربي", "نقاش تقني مفتوح", "u3", 8, listOf("u1" to RoomRole.Member, "u6" to RoomRole.Member, "u7" to RoomRole.Member))
        seed("تعلّم الإنجليزية", "تدريب على المحادثة", "u4", 6, listOf("u2" to RoomRole.Moderator))
        seed("ألعاب وضحك", null, "u5", 8, listOf("u8" to RoomRole.Member, "u6" to RoomRole.Member, "u7" to RoomRole.Member, "u1" to RoomRole.Member))
    }

    private fun seed(title: String, topic: String?, owner: String, max: Int, others: List<Pair<String, RoomRole>>) {
        val r = DemoRoom("room-${++counter}", title, topic, owner, max)
        r.seats[0] = SeatInfo(0, owner, RoomRole.Owner)
        others.forEachIndexed { i, (uid, role) -> r.seats[i + 1] = SeatInfo(i + 1, uid, role) }
        rooms[r.id] = r
    }

    fun <T> locked(block: DemoWorld.() -> T): T = synchronized(this) { block() }
}

class DemoAuthRepository(private val world: DemoWorld) : AuthRepository {
    private val user = MutableStateFlow<AuthUser?>(null)
    override val authState: Flow<AuthUser?> = user

    /** الدخول = مستخدم قديم (له ملف شخصي) فيذهب مباشرة للغرف. */
    override suspend fun signIn(email: String, password: String) {
        delay(500)
        world.locked { profiles[DemoData.ME] = DemoData.existingMe }
        user.value = AuthUser(DemoData.ME, email.trim())
    }

    /** إنشاء حساب = مستخدم جديد فتظهر شاشة إعداد الملف الشخصي. */
    override suspend fun signUp(email: String, password: String) {
        delay(500)
        world.locked { profiles.remove(DemoData.ME) }
        user.value = AuthUser(DemoData.ME, email.trim())
    }

    override fun signOut() {
        user.value = null
    }
}

class DemoProfileRepository(private val world: DemoWorld) : ProfileRepository {
    override suspend fun getProfile(uid: String): UserProfile? {
        delay(200)
        return world.locked { profiles[uid] }
    }

    override suspend fun createProfile(uid: String, draft: ProfileDraft) {
        delay(400)
        val name = draft.username.trim().lowercase()
        world.locked {
            if (profiles.values.any { it.uid != uid && it.username == name }) throw UsernameTakenException()
            profiles[uid] = UserProfile(uid, name, draft.displayName.trim(), draft.bio.trim())
        }
    }
}

class DemoRoomsRepository(private val world: DemoWorld) : RoomsRepository {

    private fun DemoWorld.room(id: String): DemoRoom =
        rooms[id]?.takeIf { it.open } ?: throw ApiException(404, "room_not_found")

    private fun DemoRoom.myRole(): RoomRole =
        if (ownerUid == DemoData.ME) RoomRole.Owner
        else seats.values.firstOrNull { it.uid == DemoData.ME }?.role ?: RoomRole.Member

    private fun DemoRoom.mySeat(): Int? = seats.values.firstOrNull { it.uid == DemoData.ME }?.seat

    private fun DemoRoom.requireStaff() {
        if (!myRole().isStaff) throw ApiException(403, "staff_only")
    }

    override suspend fun getActiveRooms(): List<VoiceRoom> {
        delay(500)
        return world.locked {
            rooms.values.filter { it.open }.map {
                VoiceRoom(it.id, it.title, it.topic, it.ownerUid, it.seats.size, it.maxSeats)
            }
        }
    }

    override suspend fun createRoom(title: String, topic: String?): VoiceRoom {
        delay(500)
        val created = world.locked {
            if (rooms.values.any { it.open && it.ownerUid == DemoData.ME }) throw ApiException(409, "active_room_exists")
            val r = DemoRoom("room-${++counter}", title.trim(), topic?.trim()?.ifBlank { null }, DemoData.ME, 8)
            r.seats[0] = SeatInfo(0, DemoData.ME, RoomRole.Owner)
            rooms[r.id] = r
            r
        }
        // طلبات صعود تصل لاحقاً لتجربة واجهة الإشراف
        world.scope.launch {
            for (uid in listOf("u6", "u4", "u7")) {
                delay(5_000)
                world.locked {
                    if (created.open && created.requests.none { it.uid == uid } && created.seats.values.none { it.uid == uid }) {
                        created.requests += SeatRequest("req-${++counter}", uid)
                    }
                }
            }
        }
        return VoiceRoom(created.id, created.title, created.topic, DemoData.ME, 1, created.maxSeats)
    }

    override suspend fun getRoomState(roomId: String): RoomState {
        delay(200)
        return world.locked {
            val r = room(roomId)
            if (DemoData.ME in r.banned) throw ApiException(403, "banned_from_room")
            RoomState(r.id, r.title, r.topic, r.ownerUid, r.maxSeats, r.myRole(), r.mySeat(), r.seats.values.sortedBy { it.seat })
        }
    }

    override suspend fun join(roomId: String): JoinInfo {
        delay(300)
        return world.locked {
            val r = room(roomId)
            if (DemoData.ME in r.banned) throw ApiException(403, "banned_from_room")
            JoinInfo("demo-token", "wss://demo.voiceup.local", r.myRole(), r.mySeat())
        }
    }

    override suspend fun closeRoom(roomId: String) {
        world.locked {
            val r = room(roomId)
            if (r.ownerUid != DemoData.ME) throw ApiException(403, "owner_only")
            r.open = false
        }
    }

    override suspend fun requestSeat(roomId: String) {
        val r = world.locked {
            val r = room(roomId)
            if (r.mySeat() != null) throw ApiException(409, "already_seated")
            if (r.freeSeat() == null) throw ApiException(409, "no_free_seat")
            if (r.requests.none { it.uid == DemoData.ME }) r.requests += SeatRequest("req-${++counter}", DemoData.ME)
            r
        }
        // موافقة تلقائية بعد 3 ثوانٍ لتجربة الانتقال لمقعد
        world.scope.launch {
            delay(3_000)
            world.locked {
                val req = r.requests.firstOrNull { it.uid == DemoData.ME }
                val free = r.freeSeat()
                if (r.open && req != null && free != null) {
                    r.requests.remove(req)
                    r.seats[free] = SeatInfo(free, DemoData.ME, RoomRole.Member)
                }
            }
        }
    }

    override suspend fun listSeatRequests(roomId: String): List<SeatRequest> = world.locked {
        val r = room(roomId)
        r.requireStaff()
        r.requests.toList()
    }

    override suspend fun decideSeatRequest(roomId: String, requestId: String, approve: Boolean) {
        world.locked {
            val r = room(roomId)
            r.requireStaff()
            val req = r.requests.firstOrNull { it.id == requestId } ?: return@locked
            r.requests.remove(req)
            if (approve) {
                val free = r.freeSeat() ?: throw ApiException(409, "no_free_seat")
                r.seats[free] = SeatInfo(free, req.uid, RoomRole.Member)
            }
        }
    }

    override suspend fun leaveSeat(roomId: String) {
        world.locked {
            val r = room(roomId)
            if (r.ownerUid == DemoData.ME) throw ApiException(409, "owner_cannot_leave_seat")
            val seat = r.mySeat() ?: throw ApiException(409, "not_seated")
            r.seats.remove(seat)
        }
    }

    override suspend fun mute(roomId: String, uid: String) {
        world.locked { room(roomId).requireStaff() }
    }

    override suspend fun kick(roomId: String, uid: String, ban: Boolean) {
        world.locked {
            val r = room(roomId)
            r.requireStaff()
            val seat = r.seats.values.firstOrNull { it.uid == uid }?.seat
            if (seat != null) r.seats.remove(seat)
            r.requests.removeAll { it.uid == uid }
            if (ban) r.banned += uid
        }
    }
}

/** محرك صوت وهمي: يحاكي الاتصال ويُظهر لاعبين "يتحدثون" عشوائياً لمعاينة تأثير الحلقة البنفسجية. */
class DemoVoiceEngine : VoiceEngine {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private val _state = MutableStateFlow(VoiceState.Disconnected)
    override val state: StateFlow<VoiceState> = _state.asStateFlow()
    private val _speaking = MutableStateFlow<Set<String>>(emptySet())
    override val speaking: StateFlow<Set<String>> = _speaking.asStateFlow()

    @Volatile private var mic = false
    private var job: Job? = null

    override suspend fun connect(url: String, token: String) {
        job?.cancel()
        _state.value = VoiceState.Connecting
        delay(700)
        _state.value = VoiceState.Connected
        val pool = DemoData.people.map { it.uid }
        job = scope.launch {
            while (true) {
                delay(1_400)
                val now = pool.shuffled().take(Random.nextInt(0, 3)).toMutableSet()
                if (mic && Random.nextBoolean()) now += DemoData.ME
                _speaking.value = now
            }
        }
    }

    override suspend fun setMicEnabled(enabled: Boolean) {
        mic = enabled
    }

    override fun disconnect() {
        job?.cancel()
        job = null
        _speaking.value = emptySet()
        _state.value = VoiceState.Disconnected
    }
}
