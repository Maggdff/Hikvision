package com.voiceup.app

import com.voiceup.app.data.api.HttpEconomyRepository
import com.voiceup.app.domain.model.ApiException
import com.voiceup.app.domain.model.GiftTier
import kotlinx.coroutines.runBlocking
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Before
import org.junit.Test

class HttpEconomyRepositoryTest {

    private lateinit var server: MockWebServer
    private lateinit var repo: HttpEconomyRepository

    @Before fun setUp() {
        server = MockWebServer().apply { start() }
        repo = HttpEconomyRepository(server.url("/").toString()) { "firebase-id-token" }
    }

    @After fun tearDown() { runCatching { server.shutdown() } }

    private fun json(body: String, code: Int = 200) =
        MockResponse().setResponseCode(code).setHeader("Content-Type", "application/json").setBody(body)

    @Test fun balanceIsReadFromWalletEndpoint() = runBlocking {
        server.enqueue(json("""{"balance":1250}"""))
        assertEquals(1250L, repo.getBalance())
        val req = server.takeRequest()
        assertEquals("GET", req.method)
        assertEquals("/v1/wallet", req.path)
        assertEquals("Bearer firebase-id-token", req.getHeader("Authorization"))
    }

    @Test fun giftsAreParsedWithTiers() = runBlocking {
        server.enqueue(json("""{"gifts":[
            {"id":"rose","nameAr":"وردة","nameEn":"Rose","priceCoins":10,"tier":"small"},
            {"id":"palace","nameAr":"قصر","nameEn":"Palace","priceCoins":10000,"tier":"large"}]}"""))
        val g = repo.listGifts()
        assertEquals("/v1/gifts", server.takeRequest().path)
        assertEquals(listOf("rose", "palace"), g.map { it.id })
        assertEquals(GiftTier.Large, g[1].tier)
        assertEquals(10_000, g[1].priceCoins)
    }

    @Test fun sendGiftSendsIdempotencyKeyHeaderAndBody() = runBlocking {
        server.enqueue(json("""{"ok":true,"giftMessageId":7,"balance":490,"replayed":false}"""))
        val r = repo.sendGift("room-1", "crown", "u2", "key-abc-12345")
        val req = server.takeRequest()
        assertEquals("POST", req.method)
        assertEquals("/v1/rooms/room-1/gifts", req.path)
        assertEquals("key-abc-12345", req.getHeader("Idempotency-Key"))
        val body = req.body.readUtf8()
        assertTrue(body, body.contains("\"giftId\":\"crown\"") && body.contains("\"recipientUid\":\"u2\""))
        assertEquals(490L, r.balance)
        assertFalse(r.replayed)
    }

    @Test fun replayedFlagIsPreserved() = runBlocking {
        server.enqueue(json("""{"ok":true,"giftMessageId":7,"balance":490,"replayed":true}"""))
        assertTrue(repo.sendGift("room-1", "crown", "u2", "key-abc-12345").replayed)
    }

    @Test fun insufficientFundsCodeIsPreserved() = runBlocking {
        server.enqueue(json("""{"error":"insufficient_funds"}""", 402))
        try {
            repo.sendGift("room-1", "crown", "u2", "key-abc-12345")
            fail("expected ApiException")
        } catch (e: ApiException) {
            assertEquals(402, e.status)
            assertEquals("insufficient_funds", e.code)
        }
    }

    @Test fun levelsAreParsedFromLevelsEndpoint() = runBlocking {
        server.enqueue(json("""{"wealth":{"points":500,"level":4,"nextAt":800},"charm":{"points":0,"level":1,"nextAt":50}}"""))
        val l = repo.getLevels()
        assertEquals("/v1/levels", server.takeRequest().path)
        assertEquals(4, l.wealth.level)
        assertEquals(800L, l.wealth.nextAt)
        assertEquals(1, l.charm.level)
    }
}
