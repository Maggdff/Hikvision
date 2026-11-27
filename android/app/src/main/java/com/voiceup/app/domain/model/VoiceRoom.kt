package com.voiceup.app.domain.model

data class VoiceRoom(
    val id: String,
    val title: String,
    val topic: String?,
    val ownerUid: String,
    val seatedCount: Int,
    val maxSeats: Int,
    val country: String? = null,
    val isPrivate: Boolean = false,
    /** وقت الإنشاء بصيغة ISO-8601 (UTC)، وترتيبه النصي يطابق ترتيبه الزمني. */
    val createdAt: String = "",
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

/** points: قيمة الهدايا التي استلمها هذا الجالس داخل الغرفة (من الخادم). */
data class SeatInfo(val seat: Int, val uid: String, val role: RoomRole, val points: Long = 0)

data class RoomState(
    val id: String,
    val title: String,
    val topic: String?,
    val ownerUid: String,
    val maxSeats: Int,
    val myRole: RoomRole,
    val mySeat: Int?,
    val seats: List<SeatInfo>,
    /** وقت إنشاء الغرفة ISO-8601 لعدّاد المدة؛ فارغ إن لم يصل. */
    val createdAt: String = "",
    /** مجموع قيمة الهدايا المرسلة داخل الغرفة. */
    val giftTotal: Long = 0,
)

/** تفاصيل رسالة هدية داخل مجرى الدردشة. */
data class GiftEvent(
    val giftId: String,
    val recipientUid: String,
    val priceCoins: Int,
    val nameAr: String,
    val nameEn: String,
)

/** kind=text: body هو النص. kind=gift: gift معبّأ وbody هو معرّف الهدية. */
data class ChatMessage(
    val id: Long,
    val uid: String,
    val body: String,
    val createdAt: String,
    val gift: GiftEvent? = null,
)

data class SeatRequest(val id: String, val uid: String)

data class JoinInfo(val token: String, val url: String, val role: RoomRole, val seat: Int?)

/** خطأ من الخادم أو الشبكة. status=0 يعني لا اتصال أو إعداد ناقص، و code نص ثابت من الخادم. */
class ApiException(val status: Int, val code: String) : Exception("$status $code")
