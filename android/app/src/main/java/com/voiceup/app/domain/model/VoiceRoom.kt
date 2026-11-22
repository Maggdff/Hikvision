package com.voiceup.app.domain.model

data class VoiceRoom(
    val id: String,
    val title: String,
    val topic: String?,
    val ownerUid: String,
    val seatedCount: Int,
    val maxSeats: Int,
)

enum class RoomRole {
    Owner, Moderator, Member;

    val isStaff: Boolean get() = this != Member

    companion object {
        fun from(raw: String): RoomRole = when (raw) {
            "owner" -> Owner
            "moderator" -> Moderator
            else -> Member
        }
    }
}

data class SeatInfo(val seat: Int, val uid: String, val role: RoomRole)

data class RoomState(
    val id: String,
    val title: String,
    val topic: String?,
    val ownerUid: String,
    val maxSeats: Int,
    val myRole: RoomRole,
    val mySeat: Int?,
    val seats: List<SeatInfo>,
)

data class SeatRequest(val id: String, val uid: String)

data class JoinInfo(val token: String, val url: String, val role: RoomRole, val seat: Int?)

/** خطأ من الخادم أو الشبكة. status=0 يعني لا اتصال أو إعداد ناقص، و code نص ثابت من الخادم. */
class ApiException(val status: Int, val code: String) : Exception("$status $code")
