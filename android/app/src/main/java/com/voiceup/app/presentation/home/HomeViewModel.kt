package com.voiceup.app.presentation.home

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.voiceup.app.domain.model.ApiException
import com.voiceup.app.domain.model.VoiceRoom
import com.voiceup.app.domain.repository.RoomsRepository
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

data class CreateRoomState(
    val creating: Boolean = false,
    val errorCode: String? = null,
)

class HomeViewModel(private val rooms: RoomsRepository) : ViewModel() {

    private val _state = MutableStateFlow<HomeUiState>(HomeUiState.Loading)
    val state: StateFlow<HomeUiState> = _state.asStateFlow()

    private val _create = MutableStateFlow(CreateRoomState())
    val create: StateFlow<CreateRoomState> = _create.asStateFlow()

    init { refresh() }

    fun refresh() {
        _state.value = HomeUiState.Loading
        viewModelScope.launch {
            _state.value = try {
                val list = rooms.getActiveRooms()
                if (list.isEmpty()) HomeUiState.Empty else HomeUiState.Content(list)
            } catch (e: ApiException) {
                HomeUiState.Error(e.code)
            } catch (e: Exception) {
                HomeUiState.Error("unknown")
            }
        }
    }

    fun clearCreateError() = _create.update { it.copy(errorCode = null) }

    /** عند النجاح يُستدعى onCreated بمعرّف الغرفة الجديدة. */
    fun createRoom(title: String, topic: String, onCreated: (String) -> Unit) {
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
        _create.value = CreateRoomState(creating = true)
        viewModelScope.launch {
            try {
                val room = rooms.createRoom(t, topic.trim().ifBlank { null })
                _create.value = CreateRoomState()
                onCreated(room.id)
            } catch (e: ApiException) {
                _create.value = CreateRoomState(errorCode = e.code)
            } catch (e: Exception) {
                _create.value = CreateRoomState(errorCode = "unknown")
            }
        }
    }
}
