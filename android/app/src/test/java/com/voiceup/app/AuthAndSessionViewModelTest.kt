package com.voiceup.app

import com.voiceup.app.domain.model.AuthError
import com.voiceup.app.domain.model.AuthException
import com.voiceup.app.domain.model.AuthUser
import com.voiceup.app.domain.model.ProfileDraft
import com.voiceup.app.domain.model.UserProfile
import com.voiceup.app.domain.repository.AuthRepository
import com.voiceup.app.domain.repository.ProfileRepository
import com.voiceup.app.presentation.auth.AuthViewModel
import com.voiceup.app.presentation.session.SessionState
import com.voiceup.app.presentation.session.SessionViewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class AuthAndSessionViewModelTest {

    @Before fun setUp() = Dispatchers.setMain(UnconfinedTestDispatcher())
    @After fun tearDown() = Dispatchers.resetMain()

    private class FakeAuth(initial: AuthUser? = null) : AuthRepository {
        val flow = MutableStateFlow(initial)
        var failWith: AuthError? = null
        override val authState = flow
        override suspend fun signIn(email: String, password: String) {
            failWith?.let { throw AuthException(it) }
            flow.value = AuthUser("u1", email)
        }
        override suspend fun signUp(email: String, password: String) = signIn(email, password)
        override fun signOut() { flow.value = null }
    }

    private class FakeProfiles(var profile: UserProfile? = null, var fail: Boolean = false) : ProfileRepository {
        override suspend fun getProfile(uid: String): UserProfile? {
            if (fail) error("offline")
            return profile
        }
        override suspend fun createProfile(uid: String, draft: ProfileDraft) {}
        override suspend fun updateProfile(uid: String, displayName: String, bio: String, country: String?) {}
    }

    private val profile = UserProfile("u1", "ali", "Ali", "")

    @Test fun invalidEmailBlocksSubmit() {
        val vm = AuthViewModel(FakeAuth()).apply { onEmail("nope"); onPassword("12345678"); submit() }
        assertEquals(AuthError.InvalidEmail, vm.state.value.error)
    }

    @Test fun shortPasswordBlocksSignUp() {
        val vm = AuthViewModel(FakeAuth()).apply { toggleMode(); onEmail("a@b.co"); onPassword("123"); submit() }
        assertEquals(AuthError.WeakPassword, vm.state.value.error)
    }

    @Test fun authFailureIsReported() {
        val auth = FakeAuth().apply { failWith = AuthError.InvalidCredentials }
        val vm = AuthViewModel(auth).apply { onEmail("a@b.co"); onPassword("12345678"); submit() }
        assertEquals(AuthError.InvalidCredentials, vm.state.value.error)
        assertEquals(false, vm.state.value.loading)
    }

    @Test fun signedOutWhenNoUser() {
        val s = SessionViewModel(FakeAuth(), FakeProfiles())
        assertEquals(SessionState.SignedOut, s.state.value)
    }

    @Test fun userWithoutProfileNeedsProfile() {
        val s = SessionViewModel(FakeAuth(AuthUser("u1", "a@b.co")), FakeProfiles())
        assertEquals(SessionState.NeedsProfile("u1"), s.state.value)
    }

    @Test fun userWithProfileIsReady() {
        val s = SessionViewModel(FakeAuth(AuthUser("u1", "a@b.co")), FakeProfiles(profile))
        assertEquals(SessionState.Ready(profile), s.state.value)
    }

    @Test fun profileLoadFailureThenRetry() {
        val profiles = FakeProfiles(profile, fail = true)
        val s = SessionViewModel(FakeAuth(AuthUser("u1", "a@b.co")), profiles)
        assertTrue(s.state.value is SessionState.Error)
        profiles.fail = false
        s.reload()
        assertEquals(SessionState.Ready(profile), s.state.value)
    }

    @Test fun signOutReturnsToSignedOut() {
        val s = SessionViewModel(FakeAuth(AuthUser("u1", "a@b.co")), FakeProfiles(profile))
        s.signOut()
        assertEquals(SessionState.SignedOut, s.state.value)
    }
}
