package com.voiceup.app

import com.voiceup.app.domain.model.ApiException
import com.voiceup.app.domain.model.VoiceRoom
import com.voiceup.app.presentation.home.HomeUiState
import com.voiceup.app.presentation.home.HomeViewModel
import com.voiceup.app.presentation.home.RoomSort
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class HomeViewModelTest {

    @Before fun setUp() = Dispatchers.setMain(UnconfinedTestDispatcher())
    @After fun tearDown() = Dispatchers.resetMain()

    private val repo = FakeRoomsRepository()
    private val room = VoiceRoom("1", "t", null, "o", 1, 8)

    @Test fun emptyListShowsEmptyState() {
        assertEquals(HomeUiState.Empty, HomeViewModel(repo).state.value)
    }

    @Test fun roomsShowContent() {
        repo.rooms = listOf(room)
        assertEquals(HomeUiState.Content(listOf(room)), HomeViewModel(repo).state.value)
    }

    @Test fun apiFailureShowsErrorWithCode() {
        repo.listError = ApiException(0, "network")
        assertEquals(HomeUiState.Error("network"), HomeViewModel(repo).state.value)
    }

    @Test fun retryRecoversAfterFailure() {
        repo.listError = ApiException(0, "network")
        val vm = HomeViewModel(repo)
        repo.listError = null
        vm.refresh()
        assertEquals(HomeUiState.Empty, vm.state.value)
    }

    @Test fun shortTitleIsRejectedWithoutCallingServer() {
        val vm = HomeViewModel(repo)
        var created: String? = null
        vm.createRoom("ab", "") { created = it }
        assertEquals("invalid_title", vm.create.value.errorCode)
        assertNull(created)
        assertTrue(repo.calls.none { it.startsWith("create") })
    }

    @Test fun createSuccessNavigatesWithNewRoomId() {
        val vm = HomeViewModel(repo)
        var created: String? = null
        vm.createRoom("  غرفة السمر  ", "  ") { created = it }
        assertEquals("new-room", created)
        assertTrue("title trimmed, blank topic dropped", repo.calls.contains("create:غرفة السمر:null:null"))
        assertNull(vm.create.value.errorCode)
    }

    @Test fun serverRefusalIsShownAndCanBeCleared() {
        repo.createError = ApiException(409, "active_room_exists")
        val vm = HomeViewModel(repo)
        var created: String? = null
        vm.createRoom("غرفة جديدة", "") { created = it }
        assertEquals("active_room_exists", vm.create.value.errorCode)
        assertNull(created)
        vm.clearCreateError()
        assertNull(vm.create.value.errorCode)
    }

    private fun r(id: String, seated: Int, at: String, country: String? = null) =
        VoiceRoom(id, "t$id", null, "o$id", seated, 8, country = country, createdAt = at)

    @Test fun popularSortsBySeatedThenNewest() {
        repo.rooms = listOf(
            r("a", 2, "2026-01-01T00:00:00Z"),
            r("b", 5, "2026-01-02T00:00:00Z"),
            r("c", 2, "2026-01-03T00:00:00Z"),
        )
        val vm = HomeViewModel(repo)
        val ids = (vm.state.value as HomeUiState.Content).rooms.map { it.id }
        assertEquals(listOf("b", "c", "a"), ids)
    }

    @Test fun newestSortReordersWithoutRefetch() {
        repo.rooms = listOf(
            r("a", 9, "2026-01-01T00:00:00Z"),
            r("b", 1, "2026-01-02T00:00:00Z"),
        )
        val vm = HomeViewModel(repo)
        val listCalls = repo.listCalls
        vm.setSort(RoomSort.Newest)
        assertEquals(listOf("b", "a"), (vm.state.value as HomeUiState.Content).rooms.map { it.id })
        assertEquals(listCalls, repo.listCalls)
    }

    @Test fun countryFilterRefetchesFromServerWithThatCountry() {
        repo.rooms = listOf(r("a", 1, "x", "SA"), r("b", 1, "y", "EG"))
        val vm = HomeViewModel(repo)
        vm.setCountry("EG")
        assertEquals("EG", repo.lastCountryFilter)
        assertEquals(listOf("b"), (vm.state.value as HomeUiState.Content).rooms.map { it.id })
        vm.setCountry(null)
        assertEquals(2, (vm.state.value as HomeUiState.Content).rooms.size)
    }

    @Test fun unsupportedCountryFilterIsIgnored() {
        val vm = HomeViewModel(repo)
        val before = repo.listCalls
        vm.setCountry("US")
        assertNull(vm.filter.value.country)
        assertEquals(before, repo.listCalls)
    }

    @Test fun filterWithNoRoomsShowsEmpty() {
        repo.rooms = listOf(r("a", 1, "x", "SA"))
        val vm = HomeViewModel(repo)
        vm.setCountry("YE")
        assertEquals(HomeUiState.Empty, vm.state.value)
    }

    @Test fun createRoomPassesCountryAndRejectsUnsupported() {
        val vm = HomeViewModel(repo)
        var created: String? = null
        vm.createRoom("غرفة جديدة", "", "US") { created = it }
        assertEquals("invalid_country", vm.create.value.errorCode)
        assertNull(created)
        vm.createRoom("غرفة جديدة", "", "SA") { created = it }
        assertEquals("new-room", created)
        assertTrue(repo.calls.contains("create:غرفة جديدة:null:SA"))
    }

    @Test fun passwordMustBeSixDigitsOrEmpty() {
        val vm = HomeViewModel(repo)
        var created: String? = null
        for (bad in listOf("12345", "1234567", "abcdef")) {
            vm.createRoom("غرفة جديدة", "", null, bad) { created = it }
            assertEquals(bad, "invalid_password", vm.create.value.errorCode)
        }
        assertNull(created)
        assertTrue(repo.calls.none { it.startsWith("create") })
    }

    @Test fun validPasswordIsSentAndEmptyMeansPublic() {
        val vm = HomeViewModel(repo)
        vm.createRoom("غرفة خاصة", "", null, "123456") { }
        assertEquals("123456", repo.lastCreatePassword)
        vm.createRoom("غرفة عامة", "", null, "") { }
        assertNull(repo.lastCreatePassword)
    }
}
