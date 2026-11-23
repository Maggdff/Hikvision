package com.voiceup.app.presentation.home

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.voiceup.app.domain.model.ApiException
import com.voiceup.app.domain.model.Countries
import com.voiceup.app.domain.model.VoiceRoom
import com.voiceup.app.domain.repository.RoomsRepository
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

sealed interface HomeUiState {
    data object Loading : HomeUiState
    data object Empty : HomeUiState
    data class Error(val code: String) : HomeUiState
    data class Content(val rooms: List<VoiceRoom>) : HomeUiState
}

enum class RoomSort { Popular, Newest }

/** country=null يعني كل الدول. */
data class HomeFilter(val country: String? = null, val sort: RoomSort = RoomSort.Popular)

data class CreateRoomState(
    val creating: Boolean = false,
    val errorCode: String? = null,
)

class HomeViewModel(private val rooms: RoomsRepository) : ViewModel() {

    private val _state = MutableStateFlow<HomeUiState>(HomeUiState.Loading)
    val state: StateFlow<HomeUiState> = _state.asStateFlow()

    private val _filter = MutableStateFlow(HomeFilter())
    val filter: StateFlow<HomeFilter> = _filter.asStateFlow()

    private val _create = MutableStateFlow(CreateRoomState())
    val create: StateFlow<CreateRoomState> = _create.asStateFlow()

    private var loaded: List<VoiceRoom> = emptyList()
    private var loadJob: Job? = null

    init { refresh() }

    fun refresh() {
        _state.value = HomeUiState.Loading
        loadJob?.cancel()
        val country = _filter.value.country
        loadJob = viewModelScope.launch {
            _state.value = try {
                loaded = rooms.getActiveRooms(country)
                present()
            } catch (e: CancellationException) {
                throw e
            } catch (e: ApiException) {
                HomeUiState.Error(e.code)
            } catch (e: Exception) {
                HomeUiState.Error("unknown")
            }
        }
    }

    /** يغيّر فلتر الدولة (null = الكل) ويعيد التحميل من الخادم. */
    fun setCountry(code: String?) {
        if (code != null && !Countries.isSupported(code)) return
        if (_filter.value.country == code) return
        _filter.update { it.copy(country = code) }
        refresh()
    }

    /** الترتيب محلي على القائمة المحمّلة، دون طلب جديد. */
    fun setSort(sort: RoomSort) {
        if (_filter.value.sort == sort) return
        _filter.update { it.copy(sort = sort) }
        if (_state.value is HomeUiState.Content) _state.value = present()
    }

    private fun present(): HomeUiState {
        if (loaded.isEmpty()) return HomeUiState.Empty
        val sorted = when (_filter.value.sort) {
            RoomSort.Popular -> loaded.sortedWith(
                compareByDescending<VoiceRoom> { it.seatedCount }.thenByDescending { it.createdAt },
            )
            RoomSort.Newest -> loaded.sortedByDescending { it.createdAt }
        }
        return HomeUiState.Content(sorted)
    }

    fun clearCreateError() = _create.update { it.copy(errorCode = null) }

    /** عند النجاح يُستدعى onCreated بمعرّف الغرفة الجديدة. */
    fun createRoom(
        title: String,
        topic: String,
        country: String? = null,
        password: String? = null,
        onCreated: (String) -> Unit,
    ) {
        if (_create.value.creating) return
        val t = title.trim()
        if (t.length !in 3..60) {
            _create.value = CreateRoomState(errorCode = "invalid_title")
            return
        }
        if (topic.trim().length > 30) {
            _create.value = CreateRoomState(errorCode = "invalid_topic")
            return
        }
        if (country != null && !Countries.isSupported(country)) {
            _create.value = CreateRoomState(errorCode = "invalid_country")
            return
        }
        val pw = password?.takeIf { it.isNotEmpty() }
        if (pw != null && !Regex("^[0-9]{6}$").matches(pw)) {
            _create.value = CreateRoomState(errorCode = "invalid_password")
            return
        }
        _create.value = CreateRoomState(creating = true)
        viewModelScope.launch {
            try {
                val room = rooms.createRoom(t, topic.trim().ifBlank { null }, country, pw)
                _create.value = CreateRoomState()
                onCreated(room.id)
            } catch (e: CancellationException) {
                throw e
            } catch (e: ApiException) {
                _create.value = CreateRoomState(errorCode = e.code)
            } catch (e: Exception) {
                _create.value = CreateRoomState(errorCode = "unknown")
            }
        }
    }
}
