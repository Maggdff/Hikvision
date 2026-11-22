package com.voiceup.app.presentation.room

import android.Manifest
import android.content.pm.PackageManager
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.ExitToApp
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.EventSeat
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material.icons.filled.MicOff
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Surface
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import com.voiceup.app.presentation.components.Avatar
import com.voiceup.app.presentation.components.EmptySeat
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
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
                onReconnect, onClearError, onExit,
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
        Row(verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = { onLeaveRoom(); onExit() }) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = stringResource(R.string.room_back))
            }
            Column(Modifier.weight(1f)) {
                Text(
                    title,
                    style = MaterialTheme.typography.titleLarge,
                    fontWeight = FontWeight.Bold,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                topic?.let {
                    Text(it, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
            }
            Row(
                Modifier
                    .clip(RoundedCornerShape(50))
                    .background(MaterialTheme.colorScheme.surfaceVariant)
                    .padding(horizontal = 10.dp, vertical = 4.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(6.dp),
            ) {
                Box(
                    Modifier
                        .size(8.dp)
                        .clip(CircleShape)
                        .background(
                            when (state.voice) {
                                VoiceState.Connected -> VoiceUpColors.Success
                                VoiceState.Connecting, VoiceState.Reconnecting -> VoiceUpColors.Warning
                                else -> VoiceUpColors.Live
                            }
                        ),
                )
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
                    style = MaterialTheme.typography.labelMedium,
                )
            }
        }
        if (state.voice == VoiceState.Disconnected || state.voice == VoiceState.Failed) {
            TextButton(onClick = onReconnect) { Text(stringResource(R.string.room_reconnect)) }
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
                    uid = seat?.uid,
                    name = name,
                    role = seat?.role,
                    speaking = seat != null && seat.uid in state.speaking,
                    onClick = if (moderatable) ({ target = seat }) else null,
                )
            }
        }

        // طلبات التحدث (للمشرفين)
        if (myRole.isStaff && state.requests.isNotEmpty()) {
            Surface(
                shape = RoundedCornerShape(20.dp),
                color = MaterialTheme.colorScheme.surfaceVariant,
                modifier = Modifier.fillMaxWidth().padding(top = 8.dp),
            ) {
                Column(Modifier.padding(12.dp)) {
                    Text(stringResource(R.string.room_requests_title), style = MaterialTheme.typography.titleSmall)
                    state.requests.take(3).forEach { req ->
                        val reqName = state.names[req.uid] ?: req.uid.take(6)
                        Row(
                            Modifier.fillMaxWidth().padding(top = 6.dp),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(10.dp),
                        ) {
                            Avatar(reqName, req.uid, 36.dp)
                            Text(reqName, modifier = Modifier.weight(1f), maxLines = 1, overflow = TextOverflow.Ellipsis)
                            IconButton(onClick = { onDecide(req.id, true) }, enabled = !state.busy) {
                                Icon(
                                    Icons.Filled.Check,
                                    contentDescription = stringResource(R.string.room_approve),
                                    tint = VoiceUpColors.Success,
                                )
                            }
                            IconButton(onClick = { onDecide(req.id, false) }, enabled = !state.busy) {
                                Icon(
                                    Icons.Filled.Close,
                                    contentDescription = stringResource(R.string.room_reject),
                                    tint = MaterialTheme.colorScheme.error,
                                )
                            }
                        }
                    }
                }
            }
        }

        // شريط التحكم السفلي
        Surface(
            shape = RoundedCornerShape(28.dp),
            color = MaterialTheme.colorScheme.surfaceVariant,
            modifier = Modifier.fillMaxWidth().padding(top = 8.dp),
        ) {
            Row(
                Modifier.fillMaxWidth().padding(horizontal = 8.dp, vertical = 10.dp),
                horizontalArrangement = Arrangement.SpaceEvenly,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                ControlButton(
                    icon = Icons.AutoMirrored.Filled.ExitToApp,
                    label = stringResource(R.string.room_leave),
                    tint = MaterialTheme.colorScheme.error,
                ) { onLeaveRoom(); onExit() }

                if (mySeat != null) {
                    MicButton(
                        on = state.micOn,
                        label = stringResource(if (state.micOn) R.string.room_mic_on else R.string.room_mic_off),
                        onClick = ::toggleMic,
                    )
                    if (myRole != RoomRole.Owner) {
                        ControlButton(
                            icon = Icons.Filled.EventSeat,
                            label = stringResource(R.string.room_leave_seat),
                            tint = MaterialTheme.colorScheme.onSurface,
                            enabled = !state.busy,
                            onClick = onLeaveSeat,
                        )
                    }
                } else {
                    Button(
                        onClick = onRequestSeat,
                        enabled = !state.requestPending && !state.busy,
                        modifier = Modifier.weight(1f).padding(horizontal = 8.dp),
                    ) {
                        Icon(Icons.Filled.Mic, contentDescription = null, modifier = Modifier.size(18.dp))
                        Spacer(Modifier.width(8.dp))
                        Text(stringResource(if (state.requestPending) R.string.room_request_sent else R.string.room_request_seat))
                    }
                }

                if (myRole == RoomRole.Owner) {
                    ControlButton(
                        icon = Icons.Filled.Close,
                        label = stringResource(R.string.room_close),
                        tint = MaterialTheme.colorScheme.error,
                        enabled = !state.busy,
                    ) { confirmClose = true }
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
private fun ControlButton(
    icon: ImageVector,
    label: String,
    tint: Color,
    enabled: Boolean = true,
    onClick: () -> Unit,
) {
    Column(
        Modifier
            .clip(RoundedCornerShape(16.dp))
            .clickable(enabled = enabled, onClick = onClick)
            .padding(6.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Box(
            Modifier.size(46.dp).clip(CircleShape).background(MaterialTheme.colorScheme.surface),
            contentAlignment = Alignment.Center,
        ) {
            Icon(icon, contentDescription = label, tint = if (enabled) tint else tint.copy(alpha = 0.4f))
        }
        Text(
            label,
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
    }
}

@Composable
private fun MicButton(on: Boolean, label: String, onClick: () -> Unit) {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Box(
            Modifier
                .size(68.dp)
                .clip(CircleShape)
                .background(if (on) VoiceUpColors.AccentGradient else SolidColor(MaterialTheme.colorScheme.surface))
                .clickable(onClick = onClick),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                if (on) Icons.Filled.Mic else Icons.Filled.MicOff,
                contentDescription = label,
                tint = Color.White,
                modifier = Modifier.size(32.dp),
            )
        }
        Spacer(Modifier.height(2.dp))
        Text(label, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

@Composable
private fun SeatCell(uid: String?, name: String?, role: RoomRole?, speaking: Boolean, onClick: (() -> Unit)?) {
    Column(
        Modifier
            .fillMaxWidth()
            .let { if (onClick != null) it.clickable(onClick = onClick) else it },
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Box(Modifier.size(72.dp), contentAlignment = Alignment.Center) {
            if (name == null || uid == null) EmptySeat(60.dp) else Avatar(name, uid, 60.dp, speaking = speaking)
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
