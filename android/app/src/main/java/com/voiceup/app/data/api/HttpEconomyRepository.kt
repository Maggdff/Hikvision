package com.voiceup.app.data.api

import com.voiceup.app.domain.model.Gift
import com.voiceup.app.domain.model.GiftReceipt
import com.voiceup.app.domain.model.GiftTier
import com.voiceup.app.domain.repository.EconomyRepository
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.decodeFromString
import okhttp3.OkHttpClient

@Serializable private data class WalletDto(val balance: Long)
@Serializable private data class GiftDto(
    val id: String,
    val nameAr: String,
    val nameEn: String,
    val priceCoins: Int,
    val tier: String = "small",
)
@Serializable private data class GiftListDto(val gifts: List<GiftDto>)
@Serializable private data class SendGiftBody(val giftId: String, val recipientUid: String)
@Serializable private data class SendGiftDto(val balance: Long, val replayed: Boolean = false)

class HttpEconomyRepository(
    baseUrl: String,
    client: OkHttpClient = defaultHttpClient(),
    tokens: suspend () -> String,
) : EconomyRepository {

    private val api = ApiClient(baseUrl, client, tokens)

    override suspend fun getBalance(): Long =
        api.call("GET", "/wallet") { api.json.decodeFromString<WalletDto>(it).balance }

    override suspend fun listGifts(): List<Gift> =
        api.call("GET", "/gifts") {
            api.json.decodeFromString<GiftListDto>(it).gifts.map { g ->
                Gift(
                    g.id, g.nameAr, g.nameEn, g.priceCoins,
                    when (g.tier) { "large" -> GiftTier.Large; "medium" -> GiftTier.Medium; else -> GiftTier.Small },
                )
            }
        }

    override suspend fun sendGift(roomId: String, giftId: String, recipientUid: String, idempotencyKey: String): GiftReceipt =
        api.call(
            "POST", "/rooms/$roomId/gifts",
            body = api.json.encodeToString(SendGiftBody(giftId, recipientUid)),
            headers = mapOf("Idempotency-Key" to idempotencyKey),
        ) {
            api.json.decodeFromString<SendGiftDto>(it).let { d -> GiftReceipt(d.balance, d.replayed) }
        }
}
