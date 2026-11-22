package com.voiceup.app.presentation.home

import androidx.annotation.StringRes
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Headphones
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.voiceup.app.R
import com.voiceup.app.domain.model.VoiceRoom
import com.voiceup.app.presentation.components.Avatar
import com.voiceup.app.presentation.components.LiveBadge
import com.voiceup.app.presentation.components.SeatDots
import com.voiceup.app.presentation.components.ShimmerCard
import com.voiceup.app.presentation.components.gradientFor
import com.voiceup.app.presentation.errorMessageRes
import com.voiceup.app.presentation.theme.VoiceUpColors

/** فئات للتصفية؛ المطابقة على كلمات في عنوان الغرفة أو موضوعها. */
private enum class Category(@StringRes val label: Int, val keys: List<String>) {
    Literature(R.string.cat_literature, listOf("أدب", "شعر", "شاعر", "قصيد")),
    Tech(R.string.cat_tech, listOf("تقن", "ذكاء", "برمج", "tech")),
    Games(R.string.cat_games, listOf("ألعاب", "لعب", "game")),
    Languages(R.string.cat_languages, listOf("إنجليز", "لغ", "english")),
}

@Composable
fun HomeScreen(
    userName: String,
    state: HomeUiState,
    create: CreateRoomState,
    onRetry: () -> Unit,
    onOpenRoom: (String) -> Unit,
    onCreate: (title: String, topic: String) -> Unit,
    onClearCreateError: () -> Unit,
) {
    var showCreate by rememberSaveable { mutableStateOf(false) }
    var query by rememberSaveable { mutableStateOf("") }
    var category by rememberSaveable { mutableStateOf(-1) }

    Box(
        Modifier
            .fillMaxSize()
            .background(VoiceUpColors.BackgroundGradient)
            .statusBarsPadding()
    ) {
        Column(Modifier.fillMaxSize().padding(horizontal = 20.dp)) {
            Row(
                Modifier.fillMaxWidth().padding(top = 16.dp),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Column(Modifier.weight(1f)) {
                    Text(
                        text = stringResource(R.string.home_greeting, userName),
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    Text(
                        text = stringResource(R.string.home_live_rooms),
                        style = MaterialTheme.typography.headlineMedium,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onBackground,
                    )
                }
                Avatar(userName, "me-$userName", 44.dp)
            }

            OutlinedTextField(
                value = query,
                onValueChange = { query = it },
                modifier = Modifier.fillMaxWidth().padding(top = 16.dp),
                singleLine = true,
                shape = RoundedCornerShape(28.dp),
                placeholder = { Text(stringResource(R.string.search_hint)) },
                leadingIcon = { Icon(Icons.Filled.Search, contentDescription = null) },
            )

            LazyRow(
                modifier = Modifier.padding(top = 12.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                items(Category.values().toList()) { c ->
                    FilterChip(
                        selected = category == c.ordinal,
                        onClick = { category = if (category == c.ordinal) -1 else c.ordinal },
                        label = { Text(stringResource(c.label)) },
                        colors = FilterChipDefaults.filterChipColors(
                            selectedContainerColor = VoiceUpColors.Violet,
                            selectedLabelColor = Color.White,
                        ),
                    )
                }
            }

            Box(Modifier.weight(1f).fillMaxWidth().padding(top = 8.dp), contentAlignment = Alignment.Center) {
                when (state) {
                    HomeUiState.Loading -> Column(
                        Modifier.fillMaxSize().padding(top = 4.dp),
                        verticalArrangement = Arrangement.spacedBy(12.dp),
                    ) {
                        repeat(3) { ShimmerCard(Modifier.fillMaxWidth()) }
                    }
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
                    is HomeUiState.Content -> {
                        val q = query.trim().lowercase()
                        val keys = Category.values().getOrNull(category)?.keys
                        val shown = state.rooms.filter { room ->
                            val text = (room.title + " " + (room.topic ?: "")).lowercase()
                            (q.isEmpty() || text.contains(q)) && (keys == null || keys.any { text.contains(it) })
                        }
                        if (shown.isEmpty()) Message(stringResource(R.string.home_no_results), null)
                        else RoomList(shown, onOpenRoom)
                    }
                }
            }
        }

        ExtendedFloatingActionButton(
            onClick = { showCreate = true },
            modifier = Modifier.align(Alignment.BottomEnd).padding(20.dp),
            containerColor = VoiceUpColors.Violet,
            contentColor = Color.White,
            icon = { Icon(Icons.Filled.Add, contentDescription = null) },
            text = { Text(stringResource(R.string.home_create)) },
        )
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
private fun Message(title: String, body: String?, action: String? = null, onAction: (() -> Unit)? = null) {
    Column(
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Icon(
            Icons.Filled.Headphones,
            contentDescription = null,
            tint = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.size(56.dp),
        )
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
        contentPadding = PaddingValues(top = 4.dp, bottom = 96.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        items(rooms, key = { it.id }) { room -> RoomCard(room) { onOpen(room.id) } }
    }
}

@Composable
private fun RoomCard(room: VoiceRoom, onOpen: () -> Unit) {
    Box(
        Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(24.dp))
            .background(gradientFor(room.id))
            .clickable(onClick = onOpen)
            .padding(16.dp),
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(
                Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                LiveBadge()
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                    Icon(Icons.Filled.Mic, contentDescription = null, tint = Color.White, modifier = Modifier.size(18.dp))
                    Text(
                        stringResource(R.string.home_seats, room.seatedCount, room.maxSeats),
                        color = Color.White,
                        style = MaterialTheme.typography.labelLarge,
                    )
                }
            }
            Text(
                room.title,
                style = MaterialTheme.typography.titleLarge,
                fontWeight = FontWeight.Bold,
                color = Color.White,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                room.topic ?: " ",
                color = Color.White.copy(alpha = 0.8f),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Spacer(Modifier.height(2.dp))
            SeatDots(room.seatedCount, room.maxSeats)
        }
    }
}
