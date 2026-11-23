package com.voiceup.app.presentation.room

import android.Manifest
import android.content.pm.PackageManager
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import com.voiceup.app.R
import com.voiceup.app.domain.model.ChatMessage
import com.voiceup.app.domain.model.RoomRole
import com.voiceup.app.domain.model.SeatInfo
import com.voiceup.app.domain.voice.VoiceState
import com.voiceup.app.presentation.errorMessageRes
import com.voiceup.app.presentation.theme.VoiceUpColors

private fun rank(r: RoomRole) = when (r) {
    RoomRole.Owner -> 3
    RoomRole.Moderator -> 2
    RoomRole.Member -> 1
}

/** الخادم هو من يفرض التسلسل فعلياً؛ هذا فقط لإخفاء الأزرار غير المتاحة. */
private fun canModerate(me: RoomRole, target: RoomRole) = me.isStaff && rank(me) > rank(target)

@Composable
fun RoomScreen(
    myUid: String,
    state: RoomUiState,
    onSetMic: (Boolean) -> Unit,
    onRequestSeat: () -> Unit,
    onLeaveSeat: () -> Unit,
    onLeaveRoom: () -> Unit,
    onClose: () -> Unit,
    onDecide: (requestId: String, approve: Boolean) -> Unit,
    onMute: (uid: String) -> Unit,
    onKick: (uid: String, ban: Boolean) -> Unit,
    onReconnect: () -> Unit,
    onClearError: () -> Unit,
    onSendMessage: (text: String, onSent: () -> Unit) -> Unit,
    onExit: () -> Unit,
) {
    BackHandler(enabled = state.end == null) {
        onLeaveRoom()
        onExit()
    }

    Box(
        Modifier
            .fillMaxSize()
            .background(VoiceUpColors.BackgroundGradient)
            .safeDrawingPadding()
    ) {
        val end = state.end
        val room = state.room
        when {
            end != null -> EndedView(end, onExit)
            room == null || state.loading -> CircularProgressIndicator(Modifier.align(Alignment.Center))
            else -> RoomContent(
                myUid, state, room.title, room.topic, room.maxSeats, room.myRole, room.mySeat, room.seats,
                onSetMic, onRequestSeat, onLeaveSeat, onLeaveRoom, onClose, onDecide, onMute, onKick,
                onReconnect, onClearError, onSendMessage, onExit,
            )
        }
    }
}

@Composable
private fun EndedView(end: RoomEnd, onExit: () -> Unit) {
    Column(
        Modifier.fillMaxSize().padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(16.dp, Alignment.CenterVertically),
    ) {
        Text(
            stringResource(
                when (end) {
                    RoomEnd.Closed -> R.string.room_ended_closed
                    RoomEnd.Banned -> R.string.room_ended_banned
                    RoomEnd.LoadFailed -> R.string.room_ended_failed
                    RoomEnd.WrongPassword -> R.string.room_ended_wrong_password
                    RoomEnd.TooManyAttempts -> R.string.room_ended_locked
                }
            ),
            style = MaterialTheme.typography.titleLarge,
            textAlign = TextAlign.Center,
        )
        Button(onClick = onExit) { Text(stringResource(R.string.room_back)) }
    }
}

@Composable
private fun RoomContent(
    myUid: String,
    state: RoomUiState,
    title: String,
    topic: String?,
    maxSeats: Int,
    myRole: RoomRole,
    mySeat: Int?,
    seats: List<SeatInfo>,
    onSetMic: (Boolean) -> Unit,
    onRequestSeat: () -> Unit,
    onLeaveSeat: () -> Unit,
    onLeaveRoom: () -> Unit,
    onClose: () -> Unit,
    onDecide: (String, Boolean) -> Unit,
    onMute: (String) -> Unit,
    onKick: (String, Boolean) -> Unit,
    onReconnect: () -> Unit,
    onClearError: () -> Unit,
    onSendMessage: (String, () -> Unit) -> Unit,
    onExit: () -> Unit,
) {
    val context = LocalContext.current
    var permissionDenied by remember { mutableStateOf(false) }
    var confirmClose by remember { mutableStateOf(false) }
    var target by remember { mutableStateOf<SeatInfo?>(null) }

    val permissionLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) onSetMic(true) else permissionDenied = true
    }
    fun toggleMic() {
        permissionDenied = false
        if (state.micOn) {
            onSetMic(false)
        } else if (ContextCompat.checkSelfPermission(context, Manifest.permission.RECORD_AUDIO) ==
            PackageManager.PERMISSION_GRANTED
        ) {
            onSetMic(true)
        } else {
            permissionLauncher.launch(Manifest.permission.RECORD_AUDIO)
        }
    }

    val bySeat = seats.associateBy { it.seat }

    Column(Modifier.fillMaxSize().padding(horizontal = 16.dp, vertical = 12.dp)) {
        // الرأس
        Text(title, style = MaterialTheme.typography.headlineSmall, maxLines = 1, overflow = TextOverflow.Ellipsis)
        topic?.let { Text(it, color = MaterialTheme.colorScheme.onSurfaceVariant) }
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(
                stringResource(
                    when (state.voice) {
                        VoiceState.Connecting -> R.string.voice_connecting
                        VoiceState.Connected -> R.string.voice_connected
                        VoiceState.Reconnecting -> R.string.voice_reconnecting
                        VoiceState.Disconnected -> R.string.voice_disconnected
                        VoiceState.Failed -> R.string.voice_failed
                    }
                ),
                color = if (state.voice == VoiceState.Connected) VoiceUpColors.Blue else MaterialTheme.colorScheme.onSurfaceVariant,
            )
            if (state.voice == VoiceState.Disconnected || state.voice == VoiceState.Failed) {
                TextButton(onClick = onReconnect) { Text(stringResource(R.string.room_reconnect)) }
            }
        }

        state.errorCode?.let {
            Row(
                Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(stringResource(errorMessageRes(it)), color = MaterialTheme.colorScheme.error, modifier = Modifier.weight(1f))
                TextButton(onClick = onClearError) { Text(stringResource(R.string.ok)) }
            }
        }
        if (permissionDenied) {
            Text(stringResource(R.string.room_mic_permission), color = MaterialTheme.colorScheme.error)
        }

        // المقاعد
        LazyVerticalGrid(
            columns = GridCells.Fixed(4),
            modifier = Modifier.weight(1f).fillMaxWidth().padding(top = 12.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            items((0 until maxSeats).toList()) { index ->
                val seat = bySeat[index]
                val name = seat?.let { state.names[it.uid] ?: it.uid.take(6) }
                val moderatable = seat != null && seat.uid != myUid && canModerate(myRole, seat.role)
                SeatCell(
                    name = name,
                    role = seat?.role,
                    speaking = seat != null && seat.uid in state.speaking,
                    onClick = if (moderatable) ({ target = seat }) else null,
                )
            }
        }

        ChatPanel(
            messages = state.messages,
            names = state.names,
            myUid = myUid,
            sending = state.sending,
            onSend = onSendMessage,
        )

        // طلبات التحدث (للمشرفين)
        if (myRole.isStaff && state.requests.isNotEmpty()) {
            Text(stringResource(R.string.room_requests_title), style = MaterialTheme.typography.titleMedium)
            state.requests.forEach { req ->
                Row(
                    Modifier.fillMaxWidth().padding(vertical = 4.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(state.names[req.uid] ?: req.uid.take(6), modifier = Modifier.weight(1f), maxLines = 1)
                    TextButton(onClick = { onDecide(req.id, true) }, enabled = !state.busy) {
                        Text(stringResource(R.string.room_approve))
                    }
                    TextButton(onClick = { onDecide(req.id, false) }, enabled = !state.busy) {
                        Text(stringResource(R.string.room_reject))
                    }
                }
            }
        }

        // أزرار التحكم
        Row(
            Modifier.fillMaxWidth().padding(top = 8.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            if (mySeat != null) {
                Button(onClick = ::toggleMic, modifier = Modifier.weight(1f)) {
                    Text(stringResource(if (state.micOn) R.string.room_mic_on else R.string.room_mic_off))
                }
                if (myRole != RoomRole.Owner) {
                    OutlinedButton(onClick = onLeaveSeat, enabled = !state.busy) {
                        Text(stringResource(R.string.room_leave_seat))
                    }
                }
            } else {
                Button(
                    onClick = onRequestSeat,
                    enabled = !state.requestPending && !state.busy,
                    modifier = Modifier.weight(1f),
                ) {
                    Text(stringResource(if (state.requestPending) R.string.room_request_sent else R.string.room_request_seat))
                }
            }
            OutlinedButton(onClick = { onLeaveRoom(); onExit() }) { Text(stringResource(R.string.room_leave)) }
            if (myRole == RoomRole.Owner) {
                OutlinedButton(onClick = { confirmClose = true }, enabled = !state.busy) {
                    Text(stringResource(R.string.room_close))
                }
            }
        }
    }

    // إجراءات الإشراف على عضو
    target?.let { seat ->
        val name = state.names[seat.uid] ?: seat.uid.take(6)
        AlertDialog(
            onDismissRequest = { target = null },
            title = { Text(name) },
            text = {
                Column {
                    TextButton(onClick = { onMute(seat.uid); target = null }) {
                        Text(stringResource(R.string.room_mute_user, name))
                    }
                    TextButton(onClick = { onKick(seat.uid, false); target = null }) {
                        Text(stringResource(R.string.room_kick_user, name))
                    }
                    TextButton(onClick = { onKick(seat.uid, true); target = null }) {
                        Text(stringResource(R.string.room_ban_user, name), color = MaterialTheme.colorScheme.error)
                    }
                }
            },
            confirmButton = {},
            dismissButton = { TextButton(onClick = { target = null }) { Text(stringResource(R.string.cancel)) } },
        )
    }

    if (confirmClose) {
        AlertDialog(
            onDismissRequest = { confirmClose = false },
            text = { Text(stringResource(R.string.room_close_confirm)) },
            confirmButton = {
                Button(onClick = { confirmClose = false; onClose() }) { Text(stringResource(R.string.room_close)) }
            },
            dismissButton = { TextButton(onClick = { confirmClose = false }) { Text(stringResource(R.string.cancel)) } },
        )
    }
}

@Composable
private fun SeatCell(name: String?, role: RoomRole?, speaking: Boolean, onClick: (() -> Unit)?) {
    Column(
        Modifier
            .fillMaxWidth()
            .let { if (onClick != null) it.clickable(onClick = onClick) else it },
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Box(
            Modifier
                .size(64.dp)
                .clip(CircleShape)
                .background(if (name == null) MaterialTheme.colorScheme.surface else MaterialTheme.colorScheme.surfaceVariant)
                .border(
                    width = if (speaking) 3.dp else 1.dp,
                    color = if (speaking) VoiceUpColors.Violet else MaterialTheme.colorScheme.surfaceVariant,
                    shape = CircleShape,
                ),
            contentAlignment = Alignment.Center,
        ) {
            if (name != null) {
                Text(name.take(1).uppercase(), style = MaterialTheme.typography.titleLarge)
            }
        }
        Text(
            text = name ?: stringResource(R.string.seat_empty),
            style = MaterialTheme.typography.labelMedium,
            color = if (name == null) MaterialTheme.colorScheme.onSurfaceVariant else MaterialTheme.colorScheme.onSurface,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        if (role == RoomRole.Owner || role == RoomRole.Moderator) {
            Text(
                stringResource(if (role == RoomRole.Owner) R.string.role_owner else R.string.role_moderator),
                style = MaterialTheme.typography.labelSmall,
                color = VoiceUpColors.Pink,
            )
        }
    }
}

@Composable
private fun ChatPanel(
    messages: List<ChatMessage>,
    names: Map<String, String>,
    myUid: String,
    sending: Boolean,
    onSend: (String, () -> Unit) -> Unit,
) {
    var draft by rememberSaveable { mutableStateOf("") }
    val listState = rememberLazyListState()
    LaunchedEffect(messages.lastOrNull()?.id) {
        if (messages.isNotEmpty()) listState.animateScrollToItem(messages.lastIndex)
    }

    Column(Modifier.fillMaxWidth().padding(top = 8.dp)) {
        LazyColumn(
            state = listState,
            modifier = Modifier
                .fillMaxWidth()
                .height(130.dp)
                .background(MaterialTheme.colorScheme.surface, androidx.compose.foundation.shape.RoundedCornerShape(12.dp))
                .padding(horizontal = 10.dp, vertical = 6.dp),
        ) {
            if (messages.isEmpty()) {
                item {
                    Text(
                        stringResource(R.string.chat_empty),
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        style = MaterialTheme.typography.bodySmall,
                    )
                }
            }
            items(messages, key = { it.id }) { m ->
                val name = if (m.uid == myUid) stringResource(R.string.chat_me) else (names[m.uid] ?: m.uid.take(6))
                Text(
                    text = "$name: ${m.body}",
                    style = MaterialTheme.typography.bodyMedium,
                    color = if (m.uid == myUid) VoiceUpColors.Blue else MaterialTheme.colorScheme.onSurface,
                    modifier = Modifier.padding(vertical = 2.dp),
                )
            }
        }
        Row(
            Modifier.fillMaxWidth().padding(top = 6.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            OutlinedTextField(
                value = draft,
                onValueChange = { draft = it.take(300) },
                placeholder = { Text(stringResource(R.string.chat_hint)) },
                singleLine = true,
                modifier = Modifier.weight(1f),
            )
            Button(
                onClick = { onSend(draft) { draft = "" } },
                enabled = draft.isNotBlank() && !sending,
            ) { Text(stringResource(R.string.chat_send)) }
        }
    }
}
