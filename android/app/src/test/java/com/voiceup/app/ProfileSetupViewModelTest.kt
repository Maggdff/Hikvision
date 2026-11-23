package com.voiceup.app

import com.voiceup.app.domain.model.ProfileDraft
import com.voiceup.app.domain.model.UserProfile
import com.voiceup.app.domain.model.UsernameTakenException
import com.voiceup.app.domain.repository.ProfileRepository
import com.voiceup.app.presentation.profile.ProfileFormError
import com.voiceup.app.presentation.profile.ProfileSetupViewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import java.time.LocalDate

@OptIn(ExperimentalCoroutinesApi::class)
class ProfileSetupViewModelTest {

    @Before fun setUp() = Dispatchers.setMain(UnconfinedTestDispatcher())
    @After fun tearDown() = Dispatchers.resetMain()

    private class FakeProfiles(var failWith: Exception? = null) : ProfileRepository {
        val created = mutableListOf<ProfileDraft>()
        override suspend fun getProfile(uid: String): UserProfile? = null
        override suspend fun createProfile(uid: String, draft: ProfileDraft) {
            failWith?.let { throw it }
            created += draft
        }
        override suspend fun updateProfile(uid: String, displayName: String, bio: String, country: String?) {}
    }

    private val today = LocalDate.of(2026, 11, 21)

    private fun vm(repo: FakeProfiles) = ProfileSetupViewModel("u1", repo) { today }.also {
        it.onUsername("ali_90"); it.onDisplayName("Ali")
    }

    private fun ProfileSetupViewModel.birth(d: String, m: String, y: String) {
        onDay(d); onMonth(m); onYear(y)
    }

    @Test fun adultCreatesProfile() {
        val repo = FakeProfiles()
        val v = vm(repo).apply { birth("1", "1", "1990"); submit() }
        assertTrue(v.state.value.done)
        assertEquals(1, repo.created.size)
    }

    @Test fun underageStoresNothing() {
        val repo = FakeProfiles()
        val v = vm(repo).apply { birth("22", "11", "2008"); submit() }
        assertTrue(v.state.value.underAge)
        assertFalse(v.state.value.done)
        assertTrue(repo.created.isEmpty())
    }

    @Test fun invalidDateShowsError() {
        val v = vm(FakeProfiles()).apply { birth("31", "2", "1990"); submit() }
        assertEquals(ProfileFormError.BirthDate, v.state.value.error)
    }

    @Test fun invalidUsernameShowsError() {
        val v = vm(FakeProfiles()).apply { onUsername("a"); birth("1", "1", "1990"); submit() }
        assertEquals(ProfileFormError.Username, v.state.value.error)
    }

    @Test fun takenUsernameShowsError() {
        val v = vm(FakeProfiles(UsernameTakenException())).apply { birth("1", "1", "1990"); submit() }
        assertEquals(ProfileFormError.UsernameTaken, v.state.value.error)
        assertFalse(v.state.value.done)
    }

    @Test fun unexpectedFailureShowsGenericError() {
        val v = vm(FakeProfiles(RuntimeException("x"))).apply { birth("1", "1", "1990"); submit() }
        assertEquals(ProfileFormError.Generic, v.state.value.error)
    }
}
