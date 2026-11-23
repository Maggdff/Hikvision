package com.voiceup.app.presentation.auth

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.voiceup.app.domain.model.AuthError
import com.voiceup.app.domain.model.AuthException
import com.voiceup.app.domain.repository.AuthRepository
import com.voiceup.app.domain.validation.Validators
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class AuthUiState(
    val email: String = "",
    val password: String = "",
    val isSignUp: Boolean = false,
    val loading: Boolean = false,
    val error: AuthError? = null,
    val errorDetail: String? = null,
)

class AuthViewModel(private val auth: AuthRepository) : ViewModel() {

    private val _state = MutableStateFlow(AuthUiState())
    val state: StateFlow<AuthUiState> = _state.asStateFlow()

    fun onEmail(v: String) = _state.update { it.copy(email = v, error = null, errorDetail = null) }
    fun onPassword(v: String) = _state.update { it.copy(password = v, error = null, errorDetail = null) }
    fun toggleMode() = _state.update { it.copy(isSignUp = !it.isSignUp, error = null, errorDetail = null) }

    fun submit() {
        val s = _state.value
        if (s.loading) return
        if (!Validators.isValidEmail(s.email)) {
            _state.update { it.copy(error = AuthError.InvalidEmail) }
            return
        }
        if (s.isSignUp && !Validators.isValidPassword(s.password)) {
            _state.update { it.copy(error = AuthError.WeakPassword) }
            return
        }
        _state.update { it.copy(loading = true, error = null) }
        viewModelScope.launch {
            try {
                if (s.isSignUp) auth.signUp(s.email, s.password) else auth.signIn(s.email, s.password)
                _state.update { it.copy(loading = false) }
            } catch (e: AuthException) {
                _state.update { it.copy(loading = false, error = e.error, errorDetail = e.detail) }
            } catch (e: Exception) {
                _state.update {
                    it.copy(loading = false, error = AuthError.Unknown, errorDetail = "${e.javaClass.simpleName}: ${e.message.orEmpty().take(160)}")
                }
            }
        }
    }
}
