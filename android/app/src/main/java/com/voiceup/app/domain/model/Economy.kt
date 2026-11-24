package com.voiceup.app.domain.model

enum class GiftTier { Small, Medium, Large }

data class Gift(
    val id: String,
    val nameAr: String,
    val nameEn: String,
    val priceCoins: Int,
    val tier: GiftTier,
)

/** balance: رصيد المرسل بعد الخصم. replayed: الطلب مكرر (نفس مفتاح العملية) ولم يُخصم ثانية. */
data class GiftReceipt(val balance: Long, val replayed: Boolean)
