package com.voiceup.app

import com.voiceup.app.domain.model.ApiException
import com.voiceup.app.domain.model.RoomRole
import com.voiceup.app.domain.model.SeatInfo
import com.voiceup.app.domain.model.SeatRequest
import com.voiceup.app.presentation.room.RoomEnd
import com.voiceup.app.presentation.room.RoomViewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class RoomViewModelTest {

    @Before fun setUp() = Dispatchers.setMain(UnconfinedTestDispatcher())
    @After fun tearDown() = Dispatchers.resetMain()

    private val repo = FakeRoomsRepository()
    private val voice = FakeVoiceEngine()
    private val profiles = FakeProfiles(mapOf("owner-uid" to "المالك", "u2" to "سارة"))

    // الاستطلاع الدوري بعيد جداً كي لا يتدخل؛ نستدعي refreshOnce() يدوياً
    private fun vm() = RoomViewModel("room-1", repo, profiles, voice, pollMillis = 1_000_000)

    @Test fun enteringLoadsRoomAndConnectsVoiceWithServerToken() {
        val v = vm()
        val s = v.state.value
        assertFalse(s.loading)
        assertNull(s.end)
        assertEquals(listOf("wss://voice.test" to "token-1"), voice.connects)
        assertEquals("المالك", s.names["owner-uid"])
    }

    @Test fun bannedUserNeverConnectsToVoice() {
        repo.stateError = ApiException(403, "banned_from_room")
        val v = vm()
        assertEquals(RoomEnd.Banned, v.state.value.end)
        assertTrue(voice.connects.isEmpty())
        assertTrue(repo.calls.none { it == "join" })
    }

    @Test fun closedRoomEndsSession() {
        repo.stateError = ApiException(404, "room_not_found")
        assertEquals(RoomEnd.Closed, vm().state.value.end)
    }

    @Test fun unreachableServerOnEnterShowsLoadFailed() {
        repo.stateError = ApiException(0, "network")
        assertEquals(RoomEnd.LoadFailed, vm().state.value.end)
    }

    @Test fun memberDoesNotFetchRequestsButStaffDoes() {
        vm()
        assertEquals(0, repo.listRequestsCalls)

        val staffRepo = FakeRoomsRepository().apply {
            state = roomState(myRole = RoomRole.Moderator, mySeat = 1)
            requests = listOf(SeatRequest("r1", "u2"))
        }
        val v = RoomViewModel("room-1", staffRepo, profiles, FakeVoiceEngine(), 1_000_000)
        assertEquals(listOf(SeatRequest("r1", "u2")), v.state.value.requests)
        assertEquals("سارة", v.state.value.names["u2"])
    }

    @Test fun unresolvedNamesDoNotBreakTheRoom() {
        profiles.failLookups = true
        val v = vm()
        assertNull(v.state.value.end)
        assertTrue(v.state.value.names.isEmpty())
    }

    @Test fun gainingASeatReconnectsWithAFreshToken() = runTest {
        val v = vm()
        assertEquals(1, voice.connects.size)

        repo.joinInfo = repo.joinInfo.copy(token = "token-with-publish")
        repo.state = roomState(mySeat = 2, seats = listOf(SeatInfo(0, "owner-uid", RoomRole.Owner), SeatInfo(2, "me", RoomRole.Member)))
        v.refreshOnce()

        assertEquals(2, voice.connects.size)
        assertEquals("token-with-publish", voice.connects.last().second)
        assertEquals(2, v.state.value.room?.mySeat)
    }

    @Test fun stableSeatDoesNotReconnectOnEveryPoll() = runTest {
        repo.state = roomState(mySeat = 1)
        val v = vm()
        repeat(3) { v.refreshOnce() }
        assertEquals(1, voice.connects.size)
    }

    @Test fun losingTheSeatTurnsTheMicOff() = runTest {
        repo.state = roomState(mySeat = 1)
        val v = vm()
        v.setMic(true)
        assertTrue(v.state.value.micOn)

        repo.state = roomState(mySeat = null)
        v.refreshOnce()

        assertFalse(v.state.value.micOn)
        assertEquals(false, voice.micCalls.last())
    }

    @Test fun requestSeatMarksPendingAndOnlyOnce() {
        val v = vm()
        v.requestSeat()
        assertTrue(v.state.value.requestPending)
        assertEquals(1, repo.calls.count { it == "requestSeat" })
    }

    @Test fun serverRefusalIsSurfacedAndClearable() {
        repo.actionError = ApiException(409, "already_seated")
        val v = vm()
        v.requestSeat()
        assertEquals("already_seated", v.state.value.errorCode)
        assertFalse(v.state.value.requestPending)
        v.clearError()
        assertNull(v.state.value.errorCode)
    }

    @Test fun staffDecisionAndModerationCallTheServer() {
        repo.state = roomState(myRole = RoomRole.Owner, mySeat = 0)
        val v = vm()
        v.decide("r1", true)
        v.decide("r2", false)
        v.mute("u2")
        v.kick("u2", true)
        assertEquals(
            listOf("decide:r1:true", "decide:r2:false", "mute:u2", "kick:u2:true"),
            repo.calls.filter { it != "join" },
        )
    }

    @Test fun roomClosedByOwnerDuringPollEndsAndDisconnects() = runTest {
        val v = vm()
        repo.stateError = ApiException(404, "room_not_found")
        v.refreshOnce()
        assertEquals(RoomEnd.Closed, v.state.value.end)
        assertTrue(voice.disconnects >= 1)
    }

    @Test fun beingBannedDuringPollEndsSession() = runTest {
        val v = vm()
        repo.stateError = ApiException(403, "banned_from_room")
        v.refreshOnce()
        assertEquals(RoomEnd.Banned, v.state.value.end)
    }

    @Test fun transientNetworkErrorDuringPollIsIgnored() = runTest {
        val v = vm()
        repo.stateError = ApiException(0, "network")
        v.refreshOnce()
        assertNull(v.state.value.end)
        assertNull(v.state.value.errorCode)
        assertEquals("غرفة", v.state.value.room?.title)
    }

    @Test fun ownerClosingRoomDisconnectsAndEnds() {
        repo.state = roomState(myRole = RoomRole.Owner, mySeat = 0)
        val v = vm()
        v.closeRoom()
        assertTrue(repo.calls.contains("close"))
        assertEquals(RoomEnd.Closed, v.state.value.end)
        assertTrue(voice.disconnects >= 1)
    }

    @Test fun refusedCloseKeepsRoomOpen() {
        repo.state = roomState(myRole = RoomRole.Moderator, mySeat = 1)
        repo.actionError = ApiException(403, "owner_only")
        val v = vm()
        v.closeRoom()
        assertNull(v.state.value.end)
        assertEquals("owner_only", v.state.value.errorCode)
    }

    @Test fun leavingFreesTheSeatForMembersButNotOwners() {
        repo.state = roomState(mySeat = 3)
        val member = vm()
        member.leaveRoom()
        assertTrue(repo.calls.contains("leaveSeat"))
        assertTrue(voice.disconnects >= 1)

        val ownerRepo = FakeRoomsRepository().apply { state = roomState(myRole = RoomRole.Owner, mySeat = 0) }
        RoomViewModel("room-1", ownerRepo, profiles, FakeVoiceEngine(), 1_000_000).leaveRoom()
        assertFalse(ownerRepo.calls.contains("leaveSeat"))
    }

    @Test fun listenerLeavingDoesNotCallLeaveSeat() {
        val v = vm()
        v.leaveRoom()
        assertFalse(repo.calls.contains("leaveSeat"))
    }

    @Test fun voiceConnectionFailureIsReportedAndRetryable() {
        voice.connectError = RuntimeException("boom")
        val v = vm()
        assertEquals("voice_failed", v.state.value.errorCode)
        assertNull(v.state.value.end)

        voice.connectError = null
        v.reconnect()
        assertEquals(1, voice.connects.size)
        assertNull("stale error cleared after a successful reconnect", v.state.value.errorCode)
    }

    @Test fun micFailureTurnsMicOffAndReports() {
        val v = vm()
        voice.micError = IllegalStateException("no publish permission")
        v.setMic(true)
        assertFalse(v.state.value.micOn)
        assertEquals("mic_failed", v.state.value.errorCode)
    }

    @Test fun speakingStateComesFromTheVoiceEngine() {
        val v = vm()
        voice.speaking.value = setOf("owner-uid")
        assertEquals(setOf("owner-uid"), v.state.value.speaking)
    }
}
