package com.voiceup.app.domain.repository

import com.voiceup.app.domain.model.Gift
import com.voiceup.app.domain.model.GiftReceipt

/** كل الدوال ترمي ApiException عند الفشل. */
interface EconomyRepository {
    suspend fun getBalance(): Long
    suspend fun listGifts(): List<Gift>

    /**
     * idempotencyKey: معرّف فريد للعملية. إعادة الإرسال بنفس المفتاح بعد انقطاع الشبكة
     * لا تخصم مرتين، فالخادم يعيد النتيجة نفسها.
     */
    suspend fun sendGift(roomId: String, giftId: String, recipientUid: String, idempotencyKey: String): GiftReceipt
}
