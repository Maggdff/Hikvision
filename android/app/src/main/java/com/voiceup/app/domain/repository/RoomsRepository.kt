package com.voiceup.app.domain.repository

import com.voiceup.app.domain.model.ChatMessage
import com.voiceup.app.domain.model.JoinInfo
import com.voiceup.app.domain.model.RoomState
import com.voiceup.app.domain.model.SeatRequest
import com.voiceup.app.domain.model.VoiceRoom

/** كل الدوال ترمي ApiException عند الفشل. */
interface RoomsRepository {
    /** country: رمز ISO من حرفين لتصفية الغرف، أو null لكل الدول. */
    suspend fun getActiveRooms(country: String? = null): List<VoiceRoom>
    /** password: 6 أرقام لغرفة خاصة، أو null لغرفة عامة. */
    suspend fun createRoom(title: String, topic: String?, country: String? = null, password: String? = null): VoiceRoom
    suspend fun getRoomState(roomId: String): RoomState
    /** password مطلوبة للغرف الخاصة (عدا المالك). */
    suspend fun join(roomId: String, password: String? = null): JoinInfo
    suspend fun closeRoom(roomId: String)

    suspend fun requestSeat(roomId: String)
    suspend fun listSeatRequests(roomId: String): List<SeatRequest>
    suspend fun decideSeatRequest(roomId: String, requestId: String, approve: Boolean)
    suspend fun leaveSeat(roomId: String)

    /** afterId=null: آخر الرسائل، وإلا ما بعد المعرّف. */
    suspend fun listMessages(roomId: String, afterId: Long?): List<ChatMessage>
    suspend fun sendMessage(roomId: String, body: String): ChatMessage

    suspend fun mute(roomId: String, uid: String)
    suspend fun kick(roomId: String, uid: String, ban: Boolean)
}
