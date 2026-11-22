package com.voiceup.app.presentation.profile

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.voiceup.app.domain.model.ProfileDraft
import com.voiceup.app.domain.model.UsernameTakenException
import com.voiceup.app.domain.repository.ProfileRepository
import com.voiceup.app.domain.validation.Validators
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import java.time.LocalDate

enum class ProfileFormError { Username, DisplayName, Bio, BirthDate, UsernameTaken, Generic }

data class ProfileSetupState(
    val username: String = "",
    val displayName: String = "",
    val bio: String = "",
    val day: String = "",
    val month: String = "",
    val year: String = "",
    val loading: Boolean = false,
    val error: ProfileFormError? = null,
    /** المستخدم دون 18: لا يُنشأ له ملف ولا تُحفظ أي بيانات. */
    val underAge: Boolean = false,
    val done: Boolean = false,
)

class ProfileSetupViewModel(
    private val uid: String,
    private val profiles: ProfileRepository,
    private val today: () -> LocalDate = { LocalDate.now() },
) : ViewModel() {

    private val _state = MutableStateFlow(ProfileSetupState())
    val state: StateFlow<ProfileSetupState> = _state.asStateFlow()

    fun onUsername(v: String) = _state.update { it.copy(username = v, error = null) }
    fun onDisplayName(v: String) = _state.update { it.copy(displayName = v, error = null) }
    fun onBio(v: String) = _state.update { it.copy(bio = v.take(Validators.BIO_MAX), error = null) }
    fun onDay(v: String) = _state.update { it.copy(day = v.filter(Char::isDigit).take(2), error = null) }
    fun onMonth(v: String) = _state.update { it.copy(month = v.filter(Char::isDigit).take(2), error = null) }
    fun onYear(v: String) = _state.update { it.copy(year = v.filter(Char::isDigit).take(4), error = null) }

    fun submit() {
        val s = _state.value
        if (s.loading) return

        val error = when {
            !Validators.isValidUsername(s.username) -> ProfileFormError.Username
            !Validators.isValidDisplayName(s.displayName) -> ProfileFormError.DisplayName
            !Validators.isValidBio(s.bio) -> ProfileFormError.Bio
            else -> null
        }
        if (error != null) {
            _state.update { it.copy(error = error) }
            return
        }

        val birth = Validators.parseBirthDate(s.day, s.month, s.year)
        if (birth == null || birth.isAfter(today())) {
            _state.update { it.copy(error = ProfileFormError.BirthDate) }
            return
        }
        if (!Validators.isAdult(birth, today())) {
            _state.update { it.copy(underAge = true) }
            return
        }

        _state.update { it.copy(loading = true, error = null) }
        viewModelScope.launch {
            try {
                profiles.createProfile(
                    uid,
                    ProfileDraft(s.username, s.displayName, s.bio, birth),
                )
                _state.update { it.copy(loading = false, done = true) }
            } catch (e: UsernameTakenException) {
                _state.update { it.copy(loading = false, error = ProfileFormError.UsernameTaken) }
            } catch (e: Exception) {
                _state.update { it.copy(loading = false, error = ProfileFormError.Generic) }
            }
        }
    }
}
