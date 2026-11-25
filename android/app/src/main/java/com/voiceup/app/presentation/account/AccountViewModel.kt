package com.voiceup.app.presentation.account

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.voiceup.app.domain.model.Countries
import com.voiceup.app.domain.model.Levels
import com.voiceup.app.domain.model.UserProfile
import com.voiceup.app.domain.repository.EconomyRepository
import com.voiceup.app.domain.repository.ProfileRepository
import com.voiceup.app.domain.validation.Validators
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class AccountUiState(
    val profile: UserProfile,
    val editing: Boolean = false,
    val displayName: String = "",
    val bio: String = "",
    val country: String? = null,
    val saving: Boolean = false,
    /** رصيد العملات؛ null حتى يُحمَّل. */
    val balance: Long? = null,
    val balanceFailed: Boolean = false,
    /** مستويات الثروة والجاذبية؛ null حتى تُحمَّل أو إن تعذّر التحميل (لا تُعرض أرقام مختلقة). */
    val levels: Levels? = null,
    /** invalid_name | invalid_bio | save_failed */
    val errorCode: String? = null,
)

class AccountViewModel(
    initial: UserProfile,
    private val profiles: ProfileRepository,
    private val economy: EconomyRepository,
) : ViewModel() {

    private val _state = MutableStateFlow(AccountUiState(initial))
    val state: StateFlow<AccountUiState> = _state.asStateFlow()

    init {
        refreshBalance()
        refreshLevels()
    }

    fun refreshLevels() {
        viewModelScope.launch {
            try {
                val l = economy.getLevels()
                _state.update { it.copy(levels = l) }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                // تبقى المستويات مخفية؛ لا نعرض 0 كأنها حقيقة
            }
        }
    }

    fun refreshBalance() {
        viewModelScope.launch {
            try {
                val b = economy.getBalance()
                _state.update { it.copy(balance = b, balanceFailed = false) }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                _state.update { it.copy(balanceFailed = true) }
            }
        }
    }

    fun startEdit() = _state.update {
        it.copy(
            editing = true, errorCode = null,
            displayName = it.profile.displayName, bio = it.profile.bio, country = it.profile.country,
        )
    }

    fun cancelEdit() = _state.update { if (it.saving) it else it.copy(editing = false, errorCode = null) }

    fun onDisplayName(v: String) = _state.update { it.copy(displayName = v.take(30), errorCode = null) }
    fun onBio(v: String) = _state.update { it.copy(bio = v.take(Validators.BIO_MAX), errorCode = null) }
    fun onCountry(code: String?) = _state.update { it.copy(country = code, errorCode = null) }

    fun save() {
        val s = _state.value
        if (!s.editing || s.saving) return
        if (!Validators.isValidDisplayName(s.displayName)) {
            _state.update { it.copy(errorCode = "invalid_name") }
            return
        }
        if (!Validators.isValidBio(s.bio)) {
            _state.update { it.copy(errorCode = "invalid_bio") }
            return
        }
        val country = s.country?.takeIf { Countries.isSupported(it) }
        _state.update { it.copy(saving = true, errorCode = null) }
        viewModelScope.launch {
            try {
                profiles.updateProfile(s.profile.uid, s.displayName.trim(), s.bio.trim(), country)
                _state.update {
                    it.copy(
                        saving = false, editing = false,
                        profile = it.profile.copy(displayName = s.displayName.trim(), bio = s.bio.trim(), country = country),
                    )
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                _state.update { it.copy(saving = false, errorCode = "save_failed") }
            }
        }
    }
}
