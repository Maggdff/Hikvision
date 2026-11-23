package com.voiceup.app.data.api

import com.voiceup.app.domain.model.ApiException
import com.voiceup.app.domain.model.ChatMessage
import com.voiceup.app.domain.model.JoinInfo
import com.voiceup.app.domain.model.RoomRole
import com.voiceup.app.domain.model.RoomState
import com.voiceup.app.domain.model.SeatInfo
import com.voiceup.app.domain.model.SeatRequest
import com.voiceup.app.domain.model.VoiceRoom
import com.voiceup.app.domain.repository.RoomsRepository
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.encodeToString
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import java.io.IOException
import java.util.concurrent.TimeUnit

@Serializable private data class ErrorDto(val error: String = "unknown")
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
@Serializable private data class SeatDto(val seat: Int, val uid: String, val role: String)
@Serializable private data class RoomStateDto(
    val id: String,
    val title: String,
    val topic: String? = null,
    val ownerUid: String,
    val maxSeats: Int,
    val myRole: String,
    val mySeat: Int? = null,
    val seats: List<SeatDto> = emptyList(),
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
@Serializable private data class MessageDto(val id: Long, val uid: String, val body: String, val createdAt: String = "")
@Serializable private data class MessageListDto(val messages: List<MessageDto>)
@Serializable private data class SendMessageBody(val body: String)
@Serializable private data class KickBody(val ban: Boolean)

class HttpRoomsRepository(
    private val baseUrl: String,
    private val client: OkHttpClient = OkHttpClient.Builder()
        .connectTimeout(10, TimeUnit.SECONDS)
        .callTimeout(20, TimeUnit.SECONDS)
        .build(),
    /** يوفّر Firebase ID Token الحالي، ويرمي استثناءً إن لم يكن هناك مستخدم. آخر وسيط ليقبل lambda لاحقة. */
    private val tokens: suspend () -> String,
) : RoomsRepository {

    private val json = Json { ignoreUnknownKeys = true }
    private val jsonType = "application/json; charset=utf-8".toMediaType()

    private suspend fun <T> call(
        method: String,
        path: String,
        body: String? = null,
        parse: (String) -> T,
    ): T = withContext(Dispatchers.IO) {
        if (baseUrl.isBlank()) throw ApiException(0, "not_configured")
        val token = try {
            tokens()
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            throw ApiException(401, "unauthenticated")
        }
        // GET بلا جسم، وغيره بجسم JSON (OkHttp يرفض POST/PUT بلا جسم)
        val requestBody = if (method == "GET") null else (body ?: "{}").toRequestBody(jsonType)
        val request = Request.Builder()
            .url(baseUrl.trimEnd('/') + "/v1" + path)
            .header("Authorization", "Bearer $token")
            .method(method, requestBody)
            .build()
        try {
            client.newCall(request).execute().use { resp ->
                val text = resp.body?.string().orEmpty()
                if (!resp.isSuccessful) {
                    val code = runCatching { json.decodeFromString<ErrorDto>(text).error }.getOrDefault("unknown")
                    throw ApiException(resp.code, code)
                }
                parse(text)
            }
        } catch (e: IOException) {
            throw ApiException(0, "network")
        } catch (e: kotlinx.serialization.SerializationException) {
            throw ApiException(0, "bad_response")
        }
    }

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
                seats = d.seats.map { s -> SeatInfo(s.seat, s.uid, RoomRole.from(s.role)) },
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
            json.decodeFromString<MessageListDto>(it).messages.map { m -> ChatMessage(m.id, m.uid, m.body, m.createdAt) }
        }

    override suspend fun sendMessage(roomId: String, body: String): ChatMessage =
        call("POST", "/rooms/$roomId/messages", json.encodeToString(SendMessageBody(body))) {
            json.decodeFromString<MessageDto>(it).let { m -> ChatMessage(m.id, m.uid, m.body, m.createdAt) }
        }

    override suspend fun mute(roomId: String, uid: String) = call("POST", "/rooms/$roomId/members/$uid/mute") { }

    override suspend fun kick(roomId: String, uid: String, ban: Boolean) =
        call("POST", "/rooms/$roomId/members/$uid/kick", json.encodeToString(KickBody(ban))) { }
}
