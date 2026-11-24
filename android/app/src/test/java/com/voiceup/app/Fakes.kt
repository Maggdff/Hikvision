package com.voiceup.app

import com.voiceup.app.domain.model.ApiException
import com.voiceup.app.domain.model.ChatMessage
import com.voiceup.app.domain.model.Gift
import com.voiceup.app.domain.model.GiftReceipt
import com.voiceup.app.domain.model.GiftTier
import com.voiceup.app.domain.repository.EconomyRepository
import com.voiceup.app.domain.model.JoinInfo
import com.voiceup.app.domain.model.ProfileDraft
import com.voiceup.app.domain.model.RoomRole
import com.voiceup.app.domain.model.RoomState
import com.voiceup.app.domain.model.SeatInfo
import com.voiceup.app.domain.model.SeatRequest
import com.voiceup.app.domain.model.UserProfile
import com.voiceup.app.domain.model.VoiceRoom
import com.voiceup.app.domain.repository.ProfileRepository
import com.voiceup.app.domain.repository.RoomsRepository
import com.voiceup.app.domain.voice.VoiceEngine
import com.voiceup.app.domain.voice.VoiceState
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

class FakeRoomsRepository : RoomsRepository {
    var rooms: List<VoiceRoom> = emptyList()
    var listError: Exception? = null
    var createError: ApiException? = null
    var stateError: Exception? = null
    var joinError: ApiException? = null
    var actionError: ApiException? = null

    var state: RoomState = roomState()
    var requests: List<SeatRequest> = emptyList()
    var joinInfo = JoinInfo("token-1", "wss://voice.test", RoomRole.Member, null)
    val calls = mutableListOf<String>()
    var listRequestsCalls = 0
    var lastCountryFilter: String? = null
    var lastJoinPassword: String? = null
    var lastCreatePassword: String? = null
    var messages: List<ChatMessage> = emptyList()
    var messageError: ApiException? = null
    val sentMessages = mutableListOf<String>()
    var listCalls = 0

    private fun action(name: String) {
        calls += name
        actionError?.let { throw it }
    }

    override suspend fun getActiveRooms(country: String?): List<VoiceRoom> {
        lastCountryFilter = country
        listCalls++
        listError?.let { throw it }
        return if (country == null) rooms else rooms.filter { it.country == country }
    }

    override suspend fun createRoom(title: String, topic: String?, country: String?, password: String?): VoiceRoom {
        calls += "create:$title:$topic:$country"
        lastCreatePassword = password
        createError?.let { throw it }
        return VoiceRoom("new-room", title, topic, "me", 1, 8, country)
    }

    override suspend fun getRoomState(roomId: String): RoomState {
        stateError?.let { throw it }
        return state
    }

    override suspend fun listMessages(roomId: String, afterId: Long?): List<ChatMessage> {
        messageError?.let { throw it }
        return if (afterId == null) messages else messages.filter { it.id > afterId }
    }

    override suspend fun sendMessage(roomId: String, body: String): ChatMessage {
        messageError?.let { throw it }
        sentMessages += body
        val m = ChatMessage((messages.maxOfOrNull { it.id } ?: 0L) + 1, "me", body, "")
        messages = messages + m
        return m
    }

    override suspend fun join(roomId: String, password: String?): JoinInfo {
        lastJoinPassword = password
        calls += "join"
        joinError?.let { throw it }
        return joinInfo
    }

    override suspend fun closeRoom(roomId: String) = action("close")
    override suspend fun requestSeat(roomId: String) = action("requestSeat")
    override suspend fun listSeatRequests(roomId: String): List<SeatRequest> {
        listRequestsCalls++
        return requests
    }
    override suspend fun decideSeatRequest(roomId: String, requestId: String, approve: Boolean) =
        action("decide:$requestId:$approve")
    override suspend fun leaveSeat(roomId: String) = action("leaveSeat")
    override suspend fun mute(roomId: String, uid: String) = action("mute:$uid")
    override suspend fun kick(roomId: String, uid: String, ban: Boolean) = action("kick:$uid:$ban")
}

fun roomState(
    myRole: RoomRole = RoomRole.Member,
    mySeat: Int? = null,
    seats: List<SeatInfo> = listOf(SeatInfo(0, "owner-uid", RoomRole.Owner)),
) = RoomState("room-1", "غرفة", null, "owner-uid", 8, myRole, mySeat, seats)

class FakeVoiceEngine : VoiceEngine {
    override val state = MutableStateFlow(VoiceState.Disconnected)
    override val speaking = MutableStateFlow<Set<String>>(emptySet())
    val connects = mutableListOf<Pair<String, String>>()
    val micCalls = mutableListOf<Boolean>()
    var disconnects = 0
    var connectError: Exception? = null
    var micError: Exception? = null

    override suspend fun connect(url: String, token: String) {
        connectError?.let { throw it }
        connects += url to token
        state.value = VoiceState.Connected
    }

    override suspend fun setMicEnabled(enabled: Boolean) {
        micError?.let { throw it }
        micCalls += enabled
    }

    override fun disconnect() {
        disconnects++
        state.value = VoiceState.Disconnected
    }
}

class FakeProfiles(private val names: Map<String, String> = emptyMap()) : ProfileRepository {
    var failLookups = false
    var failUpdate = false
    val updates = mutableListOf<String>()
    override suspend fun getProfile(uid: String): UserProfile? {
        if (failLookups) error("offline")
        return names[uid]?.let { UserProfile(uid, uid, it, "") }
    }
    override suspend fun createProfile(uid: String, draft: ProfileDraft) {}
    override suspend fun updateProfile(uid: String, displayName: String, bio: String, country: String?) {
        if (failUpdate) error("offline")
        updates += "$uid:$displayName:$bio:$country"
    }
}

class FakeEconomy : EconomyRepository {
    var balance = 1_000L
    var gifts = listOf(
        Gift("rose", "وردة", "Rose", 10, GiftTier.Small),
        Gift("crown", "تاج", "Crown", 500, GiftTier.Medium),
    )
    var balanceError: ApiException? = null
    var listError: ApiException? = null
    /** يُرمى مرة واحدة في أول sendGift ثم يُمسح. */
    var sendError: Exception? = null
    val sentKeys = mutableListOf<String>()
    val sent = mutableListOf<Pair<String, String>>()
    private val seenKeys = mutableSetOf<String>()
    var sendCalls = 0

    override suspend fun getBalance(): Long {
        balanceError?.let { throw it }
        return balance
    }

    override suspend fun listGifts(): List<Gift> {
        listError?.let { throw it }
        return gifts
    }

    override suspend fun sendGift(roomId: String, giftId: String, recipientUid: String, idempotencyKey: String): GiftReceipt {
        sendCalls++
        sentKeys += idempotencyKey
        sendError?.let { sendError = null; throw it }
        // محاكاة الخادم: نفس المفتاح لا يخصم مرتين
        if (!seenKeys.add(idempotencyKey)) return GiftReceipt(balance, replayed = true)
        val g = gifts.first { it.id == giftId }
        if (balance < g.priceCoins) throw ApiException(402, "insufficient_funds")
        balance -= g.priceCoins
        sent += giftId to recipientUid
        return GiftReceipt(balance, replayed = false)
    }
}
