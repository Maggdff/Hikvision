package com.voiceup.app

import com.voiceup.app.data.api.HttpRoomsRepository
import com.voiceup.app.domain.model.ApiException
import com.voiceup.app.domain.model.RoomRole
import kotlinx.coroutines.runBlocking
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Before
import org.junit.Test

class HttpRoomsRepositoryTest {

    private lateinit var server: MockWebServer
    private lateinit var repo: HttpRoomsRepository

    @Before fun setUp() {
        server = MockWebServer().apply { start() }
        repo = HttpRoomsRepository(server.url("/").toString()) { "firebase-id-token" }
    }

    @After fun tearDown() {
        runCatching { server.shutdown() }
    }

    private fun json(body: String, code: Int = 200) =
        MockResponse().setResponseCode(code).setHeader("Content-Type", "application/json").setBody(body)

    private fun apiError(block: () -> Unit): ApiException {
        try {
            block()
        } catch (e: ApiException) {
            return e
        }
        fail("expected ApiException")
        throw IllegalStateException()
    }

    @Test fun listRoomsSendsBearerTokenAndParses() = runBlocking {
        server.enqueue(json("""{"rooms":[{"id":"r1","title":"غرفة","topic":null,"ownerUid":"o","seatedCount":2,"maxSeats":8,"createdAt":"x"}]}"""))
        val rooms = repo.getActiveRooms()
        val req = server.takeRequest()
        assertEquals("GET", req.method)
        assertEquals("/v1/rooms", req.path)
        assertEquals("Bearer firebase-id-token", req.getHeader("Authorization"))
        assertEquals(1, rooms.size)
        assertEquals("غرفة", rooms[0].title)
        assertNull(rooms[0].topic)
        assertEquals(2, rooms[0].seatedCount)
    }

    @Test fun listRoomsWithCountryAddsQueryAndParsesCountryAndCreatedAt() = runBlocking {
        server.enqueue(json("""{"rooms":[{"id":"r1","title":"غرفة","topic":null,"ownerUid":"o","seatedCount":2,"maxSeats":8,"country":"SA","createdAt":"2026-01-02T03:04:05.000Z"}]}"""))
        val rooms = repo.getActiveRooms("SA")
        assertEquals("/v1/rooms?country=SA", server.takeRequest().path)
        assertEquals("SA", rooms[0].country)
        assertEquals("2026-01-02T03:04:05.000Z", rooms[0].createdAt)
    }

    @Test fun createRoomSendsCountryWhenGiven() = runBlocking {
        server.enqueue(json("""{"id":"r9","title":"جديدة","topic":null,"ownerUid":"me","seatedCount":1,"maxSeats":8,"country":"EG"}""", 201))
        val room = repo.createRoom("جديدة", null, "EG")
        assertTrue(server.takeRequest().body.readUtf8().contains("\"country\":\"EG\""))
        assertEquals("EG", room.country)
    }

    @Test fun createRoomPostsTitleAndDropsBlankTopic() = runBlocking {
        server.enqueue(json("""{"id":"r9","title":"جديدة","topic":null,"ownerUid":"me","seatedCount":1,"maxSeats":8}""", 201))
        val room = repo.createRoom("جديدة", "  ")
        val req = server.takeRequest()
        assertEquals("POST", req.method)
        assertEquals("/v1/rooms", req.path)
        val body = req.body.readUtf8()
        assertTrue(body, body.contains("\"title\":\"جديدة\""))
        assertTrue(body, !body.contains("topic"))
        assertEquals("r9", room.id)
    }

    @Test fun roomStateParsesRolesAndSeats() = runBlocking {
        server.enqueue(json("""{"id":"r1","title":"t","topic":"x","ownerUid":"o","maxSeats":8,"myRole":"moderator","mySeat":1,
            "seats":[{"seat":0,"uid":"o","role":"owner"},{"seat":1,"uid":"me","role":"moderator"}]}"""))
        val s = repo.getRoomState("r1")
        assertEquals("/v1/rooms/r1", server.takeRequest().path)
        assertEquals(RoomRole.Moderator, s.myRole)
        assertEquals(1, s.mySeat)
        assertEquals(listOf(RoomRole.Owner, RoomRole.Moderator), s.seats.map { it.role })
    }

    @Test fun joinParsesTokenAndNullSeat() = runBlocking {
        server.enqueue(json("""{"token":"lk-jwt","url":"wss://x.livekit.cloud","role":"member","seat":null}"""))
        val j = repo.join("r1")
        assertEquals("POST", server.takeRequest().method)
        assertEquals("lk-jwt", j.token)
        assertEquals("wss://x.livekit.cloud", j.url)
        assertEquals(RoomRole.Member, j.role)
        assertNull(j.seat)
    }

    @Test fun seatRequestEndpoints() = runBlocking {
        server.enqueue(json("""{"id":"q1","roomId":"r1","uid":"u","status":"pending","createdAt":"x"}""", 201))
        server.enqueue(json("""{"requests":[{"id":"q1","roomId":"r1","uid":"u2","status":"pending","createdAt":"x"}]}"""))
        server.enqueue(json("""{"ok":true,"voiceSynced":true,"seat":1}"""))
        server.enqueue(json("""{"ok":true,"voiceSynced":true}"""))
        repo.requestSeat("r1")
        assertEquals("/v1/rooms/r1/seat-requests", server.takeRequest().path)
        assertEquals("u2", repo.listSeatRequests("r1").single().uid)
        server.takeRequest()
        repo.decideSeatRequest("r1", "q1", true)
        assertEquals("/v1/rooms/r1/seat-requests/q1/approve", server.takeRequest().path)
        repo.decideSeatRequest("r1", "q1", false)
        assertEquals("/v1/rooms/r1/seat-requests/q1/reject", server.takeRequest().path)
    }

    @Test fun moderationEndpoints() = runBlocking {
        repeat(4) { server.enqueue(json("""{"ok":true,"voiceSynced":true}""")) }
        repo.mute("r1", "u2")
        assertEquals("/v1/rooms/r1/members/u2/mute", server.takeRequest().path)
        repo.kick("r1", "u2", true)
        val kick = server.takeRequest()
        assertEquals("/v1/rooms/r1/members/u2/kick", kick.path)
        assertTrue(kick.body.readUtf8().contains("\"ban\":true"))
        repo.leaveSeat("r1")
        assertEquals("/v1/rooms/r1/seat/leave", server.takeRequest().path)
        repo.closeRoom("r1")
        assertEquals("/v1/rooms/r1/close", server.takeRequest().path)
    }

    @Test fun serverErrorCodeIsPreserved() = runBlocking {
        server.enqueue(json("""{"error":"active_room_exists"}""", 409))
        val e = apiError { runBlocking { repo.createRoom("غرفة", null) } }
        assertEquals(409, e.status)
        assertEquals("active_room_exists", e.code)
    }

    @Test fun nonJsonErrorBodyBecomesUnknown() = runBlocking {
        server.enqueue(MockResponse().setResponseCode(502).setBody("<html>Bad Gateway</html>"))
        val e = apiError { runBlocking { repo.getActiveRooms() } }
        assertEquals(502, e.status)
        assertEquals("unknown", e.code)
    }

    @Test fun malformedSuccessBodyIsBadResponseNotACrash() = runBlocking {
        server.enqueue(json("""{"unexpected":true}"""))
        assertEquals("bad_response", apiError { runBlocking { repo.getActiveRooms() } }.code)
    }

    @Test fun unreachableServerIsNetworkError() = runBlocking {
        server.shutdown()
        val e = apiError { runBlocking { repo.getActiveRooms() } }
        assertEquals(0, e.status)
        assertEquals("network", e.code)
    }

    @Test fun missingBaseUrlMeansNotConfiguredAndSendsNothing() = runBlocking {
        val unconfigured = HttpRoomsRepository("") { "t" }
        assertEquals("not_configured", apiError { runBlocking { unconfigured.getActiveRooms() } }.code)
        assertEquals(0, server.requestCount)
    }

    @Test fun missingFirebaseTokenIsUnauthenticatedAndSendsNothing() = runBlocking {
        val noUser = HttpRoomsRepository(server.url("/").toString()) { error("not signed in") }
        val e = apiError { runBlocking { noUser.getActiveRooms() } }
        assertEquals(401, e.status)
        assertEquals("unauthenticated", e.code)
        assertEquals(0, server.requestCount)
    }
}
