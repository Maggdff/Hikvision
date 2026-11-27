package com.voiceup.app.data.api

import com.voiceup.app.domain.model.ApiException
import com.voiceup.app.domain.model.ChatMessage
import com.voiceup.app.domain.model.GiftEvent
import com.voiceup.app.domain.model.JoinInfo
import com.voiceup.app.domain.model.RoomRole
import com.voiceup.app.domain.model.RoomState
import com.voiceup.app.domain.model.SeatInfo
import com.voiceup.app.domain.model.SeatRequest
import com.voiceup.app.domain.model.VoiceRoom
import com.voiceup.app.domain.repository.RoomsRepository
import kotlinx.serialization.Serializable
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.encodeToString
import okhttp3.OkHttpClient

@Serializable private data class RoomDto(
    val id: String,
    val title: String,
    val topic: String? = null,
    val ownerUid: String,
    val seatedCount: Int = 0,
    val maxSeats: Int,
    val country: String? = null,
    val isPrivate: Boolean = false,
    val createdAt: String = "",
)
@Serializable private data class RoomListDto(val rooms: List<RoomDto>)
@Serializable private data class SeatDto(val seat: Int, val uid: String, val role: String, val points: Long = 0)
@Serializable private data class RoomStateDto(
    val id: String,
    val title: String,
    val topic: String? = null,
    val ownerUid: String,
    val maxSeats: Int,
    val myRole: String,
    val mySeat: Int? = null,
    val seats: List<SeatDto> = emptyList(),
    val createdAt: String = "",
    val giftTotal: Long = 0,
)
@Serializable private data class JoinDto(val token: String, val url: String, val role: String, val seat: Int? = null)
@Serializable private data class SeatRequestDto(val id: String, val uid: String)
@Serializable private data class SeatRequestListDto(val requests: List<SeatRequestDto>)
@Serializable private data class CreateRoomBody(
    val title: String,
    val topic: String? = null,
    val country: String? = null,
    val password: String? = null,
)
@Serializable private data class JoinBody(val password: String? = null)
@Serializable private data class GiftMetaDto(
    val giftId: String = "",
    val recipientUid: String = "",
    val priceCoins: Int = 0,
    val nameAr: String = "",
    val nameEn: String = "",
)
@Serializable private data class MessageDto(
    val id: Long,
    val uid: String,
    val body: String,
    val kind: String = "text",
    val meta: GiftMetaDto? = null,
    val createdAt: String = "",
)
@Serializable private data class MessageListDto(val messages: List<MessageDto>)
@Serializable private data class SendMessageBody(val body: String)
@Serializable private data class KickBody(val ban: Boolean)

class HttpRoomsRepository(
    baseUrl: String,
    client: OkHttpClient = defaultHttpClient(),
    /** يوفّر Firebase ID Token الحالي، ويرمي استثناءً إن لم يكن هناك مستخدم. آخر وسيط ليقبل lambda لاحقة. */
    tokens: suspend () -> String,
) : RoomsRepository {

    private val api = ApiClient(baseUrl, client, tokens)
    private val json get() = api.json

    private suspend fun <T> call(method: String, path: String, body: String? = null, parse: (String) -> T): T =
        api.call(method, path, body, emptyMap(), parse)

    private fun MessageDto.toDomain() = ChatMessage(
        id, uid, body, createdAt,
        gift = if (kind == "gift" && meta != null) GiftEvent(meta.giftId, meta.recipientUid, meta.priceCoins, meta.nameAr, meta.nameEn) else null,
    )

    private fun RoomDto.toDomain() = VoiceRoom(id, title, topic, ownerUid, seatedCount, maxSeats, country, isPrivate, createdAt)

    override suspend fun getActiveRooms(country: String?): List<VoiceRoom> =
        call("GET", if (country == null) "/rooms" else "/rooms?country=" + java.net.URLEncoder.encode(country, "UTF-8")) { json.decodeFromString<RoomListDto>(it).rooms.map { r -> r.toDomain() } }

    override suspend fun createRoom(title: String, topic: String?, country: String?, password: String?): VoiceRoom =
        call("POST", "/rooms", json.encodeToString(CreateRoomBody(title, topic?.ifBlank { null }, country, password))) {
            json.decodeFromString<RoomDto>(it).toDomain()
        }

    override suspend fun getRoomState(roomId: String): RoomState =
        call("GET", "/rooms/$roomId") {
            val d = json.decodeFromString<RoomStateDto>(it)
            RoomState(
                id = d.id, title = d.title, topic = d.topic, ownerUid = d.ownerUid, maxSeats = d.maxSeats,
                myRole = RoomRole.from(d.myRole), mySeat = d.mySeat,
                seats = d.seats.map { s -> SeatInfo(s.seat, s.uid, RoomRole.from(s.role), s.points) },
                createdAt = d.createdAt, giftTotal = d.giftTotal,
            )
        }

    override suspend fun join(roomId: String, password: String?): JoinInfo =
        call("POST", "/rooms/$roomId/join", json.encodeToString(JoinBody(password))) {
            val d = json.decodeFromString<JoinDto>(it)
            JoinInfo(d.token, d.url, RoomRole.from(d.role), d.seat)
        }

    override suspend fun closeRoom(roomId: String) = call("POST", "/rooms/$roomId/close") { }

    override suspend fun requestSeat(roomId: String) = call("POST", "/rooms/$roomId/seat-requests") { }

    override suspend fun listSeatRequests(roomId: String): List<SeatRequest> =
        call("GET", "/rooms/$roomId/seat-requests") {
            json.decodeFromString<SeatRequestListDto>(it).requests.map { r -> SeatRequest(r.id, r.uid) }
        }

    override suspend fun decideSeatRequest(roomId: String, requestId: String, approve: Boolean) =
        call("POST", "/rooms/$roomId/seat-requests/$requestId/${if (approve) "approve" else "reject"}") { }

    override suspend fun leaveSeat(roomId: String) = call("POST", "/rooms/$roomId/seat/leave") { }

    override suspend fun listMessages(roomId: String, afterId: Long?): List<ChatMessage> =
        call("GET", "/rooms/$roomId/messages" + (afterId?.let { "?after=$it" } ?: "")) {
            json.decodeFromString<MessageListDto>(it).messages.map { m -> m.toDomain() }
        }

    override suspend fun sendMessage(roomId: String, body: String): ChatMessage =
        call("POST", "/rooms/$roomId/messages", json.encodeToString(SendMessageBody(body))) {
            json.decodeFromString<MessageDto>(it).toDomain()
        }

    override suspend fun mute(roomId: String, uid: String) = call("POST", "/rooms/$roomId/members/$uid/mute") { }

    override suspend fun kick(roomId: String, uid: String, ban: Boolean) =
        call("POST", "/rooms/$roomId/members/$uid/kick", json.encodeToString(KickBody(ban))) { }
}
