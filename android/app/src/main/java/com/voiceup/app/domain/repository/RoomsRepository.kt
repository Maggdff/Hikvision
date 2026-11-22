package com.voiceup.app.domain.repository

import com.voiceup.app.domain.model.JoinInfo
import com.voiceup.app.domain.model.RoomState
import com.voiceup.app.domain.model.SeatRequest
import com.voiceup.app.domain.model.VoiceRoom

/** كل الدوال ترمي ApiException عند الفشل. */
interface RoomsRepository {
    suspend fun getActiveRooms(): List<VoiceRoom>
    suspend fun createRoom(title: String, topic: String?): VoiceRoom
    suspend fun getRoomState(roomId: String): RoomState
    suspend fun join(roomId: String): JoinInfo
    suspend fun closeRoom(roomId: String)

    suspend fun requestSeat(roomId: String)
    suspend fun listSeatRequests(roomId: String): List<SeatRequest>
    suspend fun decideSeatRequest(roomId: String, requestId: String, approve: Boolean)
    suspend fun leaveSeat(roomId: String)

    suspend fun mute(roomId: String, uid: String)
    suspend fun kick(roomId: String, uid: String, ban: Boolean)
}
