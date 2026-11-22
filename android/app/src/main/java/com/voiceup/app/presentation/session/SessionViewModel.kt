package com.voiceup.app.presentation.session

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.voiceup.app.domain.model.UserProfile
import com.voiceup.app.domain.repository.AuthRepository
import com.voiceup.app.domain.repository.ProfileRepository
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

sealed interface SessionState {
    data object Loading : SessionState
    data object SignedOut : SessionState
    data class NeedsProfile(val uid: String) : SessionState
    data class Ready(val profile: UserProfile) : SessionState
    data object Error : SessionState
}

class SessionViewModel(
    private val auth: AuthRepository,
    private val profiles: ProfileRepository,
) : ViewModel() {

    private val _state = MutableStateFlow<SessionState>(SessionState.Loading)
    val state: StateFlow<SessionState> = _state.asStateFlow()

    private var currentUid: String? = null

    init {
        viewModelScope.launch {
            auth.authState.collect { user ->
                currentUid = user?.uid
                if (user == null) _state.value = SessionState.SignedOut else load(user.uid)
            }
        }
    }

    /** يُستدعى بعد إنشاء الملف الشخصي أو عند إعادة المحاولة. */
    fun reload() {
        currentUid?.let { uid -> viewModelScope.launch { load(uid) } }
    }

    fun signOut() = auth.signOut()

    private suspend fun load(uid: String) {
        _state.value = SessionState.Loading
        _state.value = try {
            profiles.getProfile(uid)?.let { SessionState.Ready(it) } ?: SessionState.NeedsProfile(uid)
        } catch (e: Exception) {
            SessionState.Error
        }
    }
}
