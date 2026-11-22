package com.voiceup.app.presentation.home

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.voiceup.app.R
import com.voiceup.app.domain.model.VoiceRoom
import com.voiceup.app.presentation.errorMessageRes
import com.voiceup.app.presentation.theme.VoiceUpColors

@Composable
fun HomeScreen(
    state: HomeUiState,
    create: CreateRoomState,
    onRetry: () -> Unit,
    onSignOut: () -> Unit,
    onOpenRoom: (String) -> Unit,
    onCreate: (title: String, topic: String) -> Unit,
    onClearCreateError: () -> Unit,
) {
    var showCreate by rememberSaveable { mutableStateOf(false) }

    Box(
        Modifier
            .fillMaxSize()
            .background(VoiceUpColors.BackgroundGradient)
            .safeDrawingPadding()
    ) {
        Column(Modifier.fillMaxSize().padding(horizontal = 20.dp, vertical = 16.dp)) {
            Row(
                Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = stringResource(R.string.home_title),
                    style = MaterialTheme.typography.headlineMedium,
                    color = MaterialTheme.colorScheme.onBackground,
                )
                TextButton(onClick = onSignOut) { Text(stringResource(R.string.sign_out)) }
            }
            Box(Modifier.weight(1f).fillMaxWidth(), contentAlignment = Alignment.Center) {
                when (state) {
                    HomeUiState.Loading -> Loading()
                    HomeUiState.Empty -> Message(
                        stringResource(R.string.home_empty_title),
                        stringResource(R.string.home_empty_body),
                    )
                    is HomeUiState.Error -> Message(
                        stringResource(R.string.home_error),
                        stringResource(errorMessageRes(state.code)),
                        action = stringResource(R.string.home_retry),
                        onAction = onRetry,
                    )
                    is HomeUiState.Content -> RoomList(state.rooms, onOpenRoom)
                }
            }
        }

        ExtendedFloatingActionButton(
            onClick = { showCreate = true },
            modifier = Modifier.align(Alignment.BottomEnd).padding(20.dp),
        ) { Text(stringResource(R.string.home_create)) }
    }

    if (showCreate) {
        CreateRoomDialog(
            state = create,
            onDismiss = {
                if (!create.creating) {
                    showCreate = false
                    onClearCreateError()
                }
            },
            onSubmit = onCreate,
        )
    }
}

@Composable
private fun CreateRoomDialog(
    state: CreateRoomState,
    onDismiss: () -> Unit,
    onSubmit: (String, String) -> Unit,
) {
    var title by rememberSaveable { mutableStateOf("") }
    var topic by rememberSaveable { mutableStateOf("") }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(R.string.create_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(
                    value = title, onValueChange = { title = it },
                    label = { Text(stringResource(R.string.create_name)) },
                    singleLine = true, modifier = Modifier.fillMaxWidth(),
                )
                OutlinedTextField(
                    value = topic, onValueChange = { topic = it },
                    label = { Text(stringResource(R.string.create_topic)) },
                    singleLine = true, modifier = Modifier.fillMaxWidth(),
                )
                state.errorCode?.let {
                    Text(stringResource(errorMessageRes(it)), color = MaterialTheme.colorScheme.error)
                }
            }
        },
        confirmButton = {
            Button(onClick = { onSubmit(title, topic) }, enabled = !state.creating) {
                if (state.creating) CircularProgressIndicator(Modifier.padding(2.dp), strokeWidth = 2.dp)
                else Text(stringResource(R.string.create_submit))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss, enabled = !state.creating) { Text(stringResource(R.string.cancel)) }
        },
    )
}

@Composable
private fun Loading() {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        CircularProgressIndicator(color = MaterialTheme.colorScheme.primary)
        Text(
            stringResource(R.string.home_loading),
            modifier = Modifier.padding(top = 12.dp),
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

@Composable
private fun Message(title: String, body: String?, action: String? = null, onAction: (() -> Unit)? = null) {
    Column(
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Text(title, style = MaterialTheme.typography.titleMedium, textAlign = TextAlign.Center)
        if (body != null) {
            Text(body, color = MaterialTheme.colorScheme.onSurfaceVariant, textAlign = TextAlign.Center)
        }
        if (action != null && onAction != null) {
            Button(onClick = onAction) { Text(action) }
        }
    }
}

@Composable
private fun RoomList(rooms: List<VoiceRoom>, onOpen: (String) -> Unit) {
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(top = 12.dp, bottom = 88.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        items(rooms, key = { it.id }) { room ->
            Surface(
                shape = RoundedCornerShape(20.dp),
                color = MaterialTheme.colorScheme.surfaceVariant,
                modifier = Modifier.fillMaxWidth().clickable { onOpen(room.id) },
            ) {
                Row(Modifier.padding(16.dp), horizontalArrangement = Arrangement.SpaceBetween) {
                    Column(Modifier.weight(1f)) {
                        Text(room.title, style = MaterialTheme.typography.titleMedium)
                        room.topic?.let { Text(it, color = MaterialTheme.colorScheme.onSurfaceVariant) }
                    }
                    Text(stringResource(R.string.home_seats, room.seatedCount, room.maxSeats))
                }
            }
        }
    }
}
