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

/** مستوى واحد: points النقاط الحالية، level المستوى، nextAt نقاط المستوى التالي (null عند الأعلى). */
data class LevelInfo(val points: Long, val level: Int, val nextAt: Long?)

/** wealth: من مجموع ما أرسله من هدايا. charm: من مجموع ما استلمه. يحسبها الخادم من السجل الفعلي. */
data class Levels(val wealth: LevelInfo, val charm: LevelInfo)
