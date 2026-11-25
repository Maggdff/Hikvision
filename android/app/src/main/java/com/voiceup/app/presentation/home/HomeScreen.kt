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
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
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
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.voiceup.app.R
import com.voiceup.app.domain.model.Countries
import com.voiceup.app.domain.model.VoiceRoom
import com.voiceup.app.presentation.countryName
import com.voiceup.app.presentation.errorMessageRes
import com.voiceup.app.presentation.theme.appBackground

@Composable
fun HomeScreen(
    myUid: String,
    state: HomeUiState,
    filter: HomeFilter,
    create: CreateRoomState,
    onRetry: () -> Unit,
    onSelectCountry: (String?) -> Unit,
    onSelectSort: (RoomSort) -> Unit,
    onOpenRoom: (roomId: String, password: String?) -> Unit,
    onCreate: (title: String, topic: String, country: String?, password: String?) -> Unit,
    onClearCreateError: () -> Unit,
) {
    var showCreate by rememberSaveable { mutableStateOf(false) }
    var showCountryFilter by rememberSaveable { mutableStateOf(false) }
    // الغرفة الخاصة التي ينتظر دخولها كلمة سر (معرّفها فقط، لا كلمة السر)
    var passwordFor by rememberSaveable { mutableStateOf<String?>(null) }

    Box(Modifier.fillMaxSize().background(appBackground())) {
        Column(Modifier.fillMaxSize().padding(horizontal = 16.dp)) {
            Text(
                text = stringResource(R.string.home_title),
                style = MaterialTheme.typography.titleLarge,
                fontWeight = androidx.compose.ui.text.font.FontWeight.Bold,
                color = MaterialTheme.colorScheme.onBackground,
                modifier = Modifier.padding(top = 18.dp, bottom = 12.dp),
            )
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                PillChip(stringResource(R.string.home_sort_popular), filter.sort == RoomSort.Popular) { onSelectSort(RoomSort.Popular) }
                PillChip(stringResource(R.string.home_sort_new), filter.sort == RoomSort.Newest) { onSelectSort(RoomSort.Newest) }
                PillChip(
                    Countries.find(filter.country)?.let { countryName(it) } ?: stringResource(R.string.country_all),
                    filter.country != null,
                ) { showCountryFilter = true }
            }
            Box(Modifier.weight(1f).fillMaxWidth(), contentAlignment = Alignment.Center) {
                when (state) {
                    HomeUiState.Loading -> Loading()
                    HomeUiState.Empty -> Message(
                        stringResource(R.string.home_empty_title),
                        Countries.find(filter.country)?.let { stringResource(R.string.home_empty_country, countryName(it)) }
                            ?: stringResource(R.string.home_empty_body),
                    )
                    is HomeUiState.Error -> Message(
                        stringResource(R.string.home_error),
                        stringResource(errorMessageRes(state.code)),
                        action = stringResource(R.string.home_retry),
                        onAction = onRetry,
                    )
                    is HomeUiState.Content -> RoomGrid(state.rooms) { room ->
                        if (room.isPrivate && room.ownerUid != myUid) passwordFor = room.id
                        else onOpenRoom(room.id, null)
                    }
                }
            }
        }

        ExtendedFloatingActionButton(
            onClick = { showCreate = true },
            modifier = Modifier.align(Alignment.BottomEnd).padding(16.dp),
            containerColor = MaterialTheme.colorScheme.primary,
            contentColor = MaterialTheme.colorScheme.onPrimary,
        ) { Text(stringResource(R.string.home_create)) }
    }

    if (showCountryFilter) {
        CountryPickerDialog(
            selected = filter.country,
            allowAll = true,
            onPick = { showCountryFilter = false; onSelectCountry(it) },
            onDismiss = { showCountryFilter = false },
        )
    }
    passwordFor?.let { id ->
        PasswordDialog(
            onSubmit = { pw -> passwordFor = null; onOpenRoom(id, pw) },
            onDismiss = { passwordFor = null },
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
fun CountryPickerDialog(
    selected: String?,
    allowAll: Boolean,
    onPick: (String?) -> Unit,
    onDismiss: () -> Unit,
    noneLabel: Int = R.string.country_all,
) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(R.string.country_pick_title)) },
        text = {
            LazyColumn(Modifier.height(360.dp)) {
                if (allowAll) {
                    item {
                        CountryRow(stringResource(noneLabel), selected == null) { onPick(null) }
                    }
                }
                items(Countries.all, key = { it.code }) { c ->
                    CountryRow(countryName(c), selected == c.code) { onPick(c.code) }
                }
            }
        },
        confirmButton = {},
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.cancel)) } },
    )
}

@Composable
private fun CountryRow(name: String, selected: Boolean, onClick: () -> Unit) {
    Text(
        text = name,
        color = if (selected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurface,
        style = MaterialTheme.typography.bodyLarge,
        modifier = Modifier.fillMaxWidth().clickable(onClick = onClick).padding(vertical = 12.dp),
    )
}

@Composable
private fun CreateRoomDialog(
    state: CreateRoomState,
    onDismiss: () -> Unit,
    onSubmit: (String, String, String?, String?) -> Unit,
) {
    var password by rememberSaveable { mutableStateOf("") }
    var title by rememberSaveable { mutableStateOf("") }
    var topic by rememberSaveable { mutableStateOf("") }
    var country by rememberSaveable { mutableStateOf<String?>(null) }
    var picking by rememberSaveable { mutableStateOf(false) }
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
                OutlinedTextField(
                    value = password,
                    onValueChange = { v -> password = v.filter { it.isDigit() }.take(6) },
                    label = { Text(stringResource(R.string.create_password)) },
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                    visualTransformation = PasswordVisualTransformation(),
                    modifier = Modifier.fillMaxWidth(),
                )
                OutlinedButton(onClick = { picking = true }, modifier = Modifier.fillMaxWidth()) {
                    Text(
                        Countries.find(country)?.let { countryName(it) }
                            ?: stringResource(R.string.create_country_none),
                    )
                }
                state.errorCode?.let {
                    Text(stringResource(errorMessageRes(it)), color = MaterialTheme.colorScheme.error)
                }
            }
        },
        confirmButton = {
            Button(onClick = { onSubmit(title, topic, country, password.ifEmpty { null }) }, enabled = !state.creating) {
                if (state.creating) CircularProgressIndicator(Modifier.padding(2.dp), strokeWidth = 2.dp)
                else Text(stringResource(R.string.create_submit))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss, enabled = !state.creating) { Text(stringResource(R.string.cancel)) }
        },
    )
    if (picking) {
        CountryPickerDialog(
            selected = country,
            allowAll = true,
            onPick = { country = it; picking = false },
            onDismiss = { picking = false },
        )
    }
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
        modifier = Modifier.padding(24.dp),
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

/** ألوان غلاف بطاقة الغرفة (خلفية، حرف) مشتقة من معرّفها، فتبقى ثابتة لنفس الغرفة. */
private val cardPalettes = listOf(
    Color(0xFF2B3A32) to Color(0xFFA9C9B5),
    Color(0xFF3A3128) to Color(0xFFD9BC95),
    Color(0xFF2F3440) to Color(0xFFA9B6D1),
    Color(0xFF3B2F36) to Color(0xFFD2AEBF),
    Color(0xFF2E3B3B) to Color(0xFFA5CFCB),
    Color(0xFF38362B) to Color(0xFFCFC9A0),
)

@Composable
private fun PillChip(label: String, selected: Boolean, onClick: () -> Unit) {
    val bg = if (selected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.surface
    val fg = if (selected) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.onSurfaceVariant
    Text(
        text = label,
        color = fg,
        style = MaterialTheme.typography.labelLarge,
        maxLines = 1,
        overflow = TextOverflow.Ellipsis,
        modifier = Modifier
            .background(bg, RoundedCornerShape(50))
            .clickable(onClick = onClick)
            .padding(horizontal = 16.dp, vertical = 8.dp),
    )
}

@Composable
private fun RoomGrid(rooms: List<VoiceRoom>, onOpen: (VoiceRoom) -> Unit) {
    LazyVerticalGrid(
        columns = GridCells.Fixed(2),
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(top = 14.dp, bottom = 88.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        items(rooms, key = { it.id }) { room -> RoomCard(room) { onOpen(room) } }
    }
}

@Composable
private fun RoomCard(room: VoiceRoom, onClick: () -> Unit) {
    val (bg, letterColor) = cardPalettes[Math.floorMod(room.id.hashCode(), cardPalettes.size)]
    Column(Modifier.fillMaxWidth().clickable(onClick = onClick)) {
        Box(
            Modifier
                .fillMaxWidth()
                .height(128.dp)
                .background(bg, RoundedCornerShape(18.dp)),
        ) {
            Text(
                text = room.title.trim().take(1),
                color = letterColor,
                style = MaterialTheme.typography.displayMedium,
                modifier = Modifier.align(Alignment.Center),
            )
            // وسم: خاصة، أو موضوع الغرفة إن وُجد
            val tag = if (room.isPrivate) stringResource(R.string.room_private_badge) else room.topic
            if (!tag.isNullOrBlank()) {
                Text(
                    text = tag,
                    color = MaterialTheme.colorScheme.primary,
                    style = MaterialTheme.typography.labelSmall,
                    fontWeight = androidx.compose.ui.text.font.FontWeight.Bold,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier
                        .align(Alignment.TopEnd)
                        .padding(8.dp)
                        .background(Color(0xCC0F1512), RoundedCornerShape(6.dp))
                        .padding(horizontal = 8.dp, vertical = 3.dp),
                )
            }
            // عدد الجالسين على المنصة (بيانات حقيقية من الخادم)
            Text(
                text = stringResource(R.string.home_seats, room.seatedCount, room.maxSeats),
                color = MaterialTheme.colorScheme.onSurface,
                style = MaterialTheme.typography.labelSmall,
                modifier = Modifier
                    .align(Alignment.BottomStart)
                    .padding(8.dp)
                    .background(Color(0xCC0F1512), RoundedCornerShape(8.dp))
                    .padding(horizontal = 8.dp, vertical = 4.dp),
            )
        }
        Row(
            Modifier.fillMaxWidth().padding(top = 8.dp, start = 2.dp, end = 2.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Text(
                room.title, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurface,
                maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f),
            )
            Countries.find(room.country)?.let {
                Text(
                    countryName(it), style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

@Composable
private fun PasswordDialog(onSubmit: (String) -> Unit, onDismiss: () -> Unit) {
    var pw by rememberSaveable { mutableStateOf("") }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(R.string.password_title)) },
        text = {
            OutlinedTextField(
                value = pw,
                onValueChange = { v -> pw = v.filter { it.isDigit() }.take(6) },
                label = { Text(stringResource(R.string.password_hint)) },
                singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                visualTransformation = PasswordVisualTransformation(),
                modifier = Modifier.fillMaxWidth(),
            )
        },
        confirmButton = {
            Button(onClick = { onSubmit(pw) }, enabled = pw.length == 6) { Text(stringResource(R.string.password_enter)) }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.cancel)) } },
    )
}
