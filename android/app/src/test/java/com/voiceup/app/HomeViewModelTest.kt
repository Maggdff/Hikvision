package com.voiceup.app

import com.voiceup.app.domain.model.ApiException
import com.voiceup.app.domain.model.VoiceRoom
import com.voiceup.app.presentation.home.HomeUiState
import com.voiceup.app.presentation.home.HomeViewModel
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
        vm.createRoom("  غرفة السمر  ", "  ", { created = it })
        assertEquals("new-room", created)
        assertTrue("title trimmed, blank topic dropped", repo.calls.contains("create:غرفة السمر:null"))
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
}
