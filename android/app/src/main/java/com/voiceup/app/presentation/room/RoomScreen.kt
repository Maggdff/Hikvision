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
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.CardGiftcard
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
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
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import com.voiceup.app.R
import com.voiceup.app.domain.model.ChatMessage
import com.voiceup.app.domain.model.RoomRole
import com.voiceup.app.domain.model.SeatInfo
import com.voiceup.app.domain.voice.VoiceState
import com.voiceup.app.presentation.components.Avatar
import com.voiceup.app.presentation.errorMessageRes
import com.voiceup.app.presentation.giftName
import com.voiceup.app.presentation.theme.VoiceUpColors
import com.voiceup.app.presentation.theme.appBackground

private data class GiftEffectRequest(val key: Long, val giftId: String, val priceCoins: Int)

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
    onLoadGifts: () -> Unit,
    onSendGift: (giftId: String, recipientUid: String) -> Unit,
    onDismissBanner: (messageId: Long) -> Unit,
    onExit: () -> Unit,
) {
    BackHandler(enabled = state.end == null) {
        onLeaveRoom()
        onExit()
    }

    Box(
        Modifier
            .fillMaxSize()
            .background(appBackground())
            .safeDrawingPadding()
    ) {
        val end = state.end
        val room = state.room
        when {
            end != null -> EndedView(end, onExit)
            room == null || state.loading -> CircularProgressIndicator(Modifier.align(Alignment.Center))
            else -> RoomContent(
                myUid, state, room.title, room.maxSeats, room.myRole, room.mySeat, room.seats,
                onSetMic, onRequestSeat, onLeaveSeat, onLeaveRoom, onClose, onDecide, onMute, onKick,
                onReconnect, onClearError, onSendMessage, onLoadGifts, onSendGift, onExit,
            )
        }
        // تأثير الهدية فوق المقاعد والدردشة؛ لا يلتقط اللمس
        var effect by remember { mutableStateOf<GiftEffectRequest?>(null) }
        LaunchedEffect(state.banner?.messageId) {
            state.banner?.let { effect = GiftEffectRequest(it.messageId, it.event.giftId, it.event.priceCoins) }
        }
        effect?.let { e ->
            GiftEffect(
                effectKey = e.key, giftId = e.giftId, priceCoins = e.priceCoins,
                modifier = Modifier.padding(top = 96.dp, bottom = 76.dp),
                onFinished = { effect = null },
            )
        }
        state.banner?.let { b ->
            GiftBannerView(b, myUid, state.names, onDismissBanner, Modifier.align(Alignment.TopCenter))
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
    onLoadGifts: () -> Unit,
    onSendGift: (String, String) -> Unit,
    onExit: () -> Unit,
) {
    var showGifts by rememberSaveable { mutableStateOf(false) }
    var showMenu by rememberSaveable { mutableStateOf(false) }
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
    val room = state.room

    fun seatClick(seat: SeatInfo?): (() -> Unit)? = when {
        // مقعد فارغ: من ليس على المنصة يطلب التحدث بلمسه
        seat == null && mySeat == null && !state.requestPending && !state.busy -> onRequestSeat
        seat != null && seat.uid != myUid && canModerate(myRole, seat.role) -> ({ target = seat })
        else -> null
    }

    Column(Modifier.fillMaxSize()) {
        RoomTopBar(title, seats.size, state.voice, onExit = { onLeaveRoom(); onExit() }, onReconnect = onReconnect)
        RoomInfoRow(room?.createdAt.orEmpty(), room?.giftTotal ?: 0L)

        state.errorCode?.let {
            Row(
                Modifier.fillMaxWidth().padding(horizontal = 14.dp),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(stringResource(errorMessageRes(it)), color = MaterialTheme.colorScheme.error, fontSize = 12.sp, modifier = Modifier.weight(1f))
                TextButton(onClick = onClearError) { Text(stringResource(R.string.ok)) }
            }
        }
        if (permissionDenied) {
            Text(
                stringResource(R.string.room_mic_permission), color = MaterialTheme.colorScheme.error, fontSize = 12.sp,
                modifier = Modifier.padding(horizontal = 14.dp),
            )
        }

        // المضيف: المقعد 0
        val host = bySeat[0]
        Box(Modifier.fillMaxWidth().padding(top = 4.dp), contentAlignment = Alignment.Center) {
            HostSeat(
                name = host?.let { state.names[it.uid] ?: it.uid.take(6) },
                seed = host?.uid,
                speaking = host != null && host.uid in state.speaking,
                points = host?.points ?: 0L,
                onClick = seatClick(host),
            )
        }

        // بقية المقاعد: 4 في كل صف
        val rest = (1 until maxSeats).toList()
        Column(Modifier.fillMaxWidth().padding(top = 14.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            rest.chunked(4).forEach { rowSeats ->
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly) {
                    rowSeats.forEach { index ->
                        val seat = bySeat[index]
                        SeatCell(
                            number = index,
                            name = seat?.let { state.names[it.uid] ?: it.uid.take(6) },
                            seed = seat?.uid,
                            speaking = seat != null && seat.uid in state.speaking,
                            points = seat?.points ?: 0L,
                            onClick = seatClick(seat),
                        )
                    }
                    // صف ناقص: نحافظ على المحاذاة بخلايا فارغة
                    repeat(4 - rowSeats.size) { Box(Modifier.width(82.dp)) }
                }
            }
        }

        RoomChat(
            messages = state.messages,
            names = state.names,
            myUid = myUid,
            modifier = Modifier.weight(1f).padding(top = 16.dp),
        )

        RoomBottomBar(
            sending = state.sending,
            onSend = onSendMessage,
            onGift = { showGifts = true },
            showMic = mySeat != null,
            micOn = state.micOn,
            onMic = ::toggleMic,
            onMenu = { showMenu = true },
            menuBadge = if (myRole.isStaff) state.requests.size else 0,
        )
    }

    if (showGifts) {
        GiftSheet(
            state = state,
            myUid = myUid,
            seats = seats,
            onLoad = onLoadGifts,
            onSend = onSendGift,
            onDismiss = { showGifts = false },
        )
    }

    if (showMenu) {
        RoomMenuSheet(
            state = state,
            myRole = myRole,
            mySeat = mySeat,
            onDismiss = { showMenu = false },
            onRequestSeat = { showMenu = false; onRequestSeat() },
            onLeaveSeat = { showMenu = false; onLeaveSeat() },
            onReconnect = { showMenu = false; onReconnect() },
            onDecide = onDecide,
            onLeaveRoom = { showMenu = false; onLeaveRoom(); onExit() },
            onClose = { showMenu = false; confirmClose = true },
        )
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

/** قائمة الغرفة (زر الشبكة): كل بنودها تعمل فعلاً حسب دور المستخدم وحالته. */
@OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
@Composable
private fun RoomMenuSheet(
    state: RoomUiState,
    myRole: RoomRole,
    mySeat: Int?,
    onDismiss: () -> Unit,
    onRequestSeat: () -> Unit,
    onLeaveSeat: () -> Unit,
    onReconnect: () -> Unit,
    onDecide: (String, Boolean) -> Unit,
    onLeaveRoom: () -> Unit,
    onClose: () -> Unit,
) {
    androidx.compose.material3.ModalBottomSheet(onDismissRequest = onDismiss) {
        Column(Modifier.padding(horizontal = 16.dp).padding(bottom = 24.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(stringResource(R.string.room_menu), style = MaterialTheme.typography.titleMedium, modifier = Modifier.padding(bottom = 8.dp))
            if (mySeat == null) {
                MenuRow(
                    stringResource(if (state.requestPending) R.string.room_request_sent else R.string.room_request_seat),
                    enabled = !state.requestPending && !state.busy, onClick = onRequestSeat,
                )
            } else if (myRole != RoomRole.Owner) {
                MenuRow(stringResource(R.string.room_leave_seat), enabled = !state.busy, onClick = onLeaveSeat)
            }
            if (state.voice == VoiceState.Disconnected || state.voice == VoiceState.Failed) {
                MenuRow(stringResource(R.string.room_reconnect), onClick = onReconnect)
            }
            if (myRole.isStaff && state.requests.isNotEmpty()) {
                Text(stringResource(R.string.room_requests_title), style = MaterialTheme.typography.labelLarge, modifier = Modifier.padding(top = 8.dp))
                state.requests.forEach { req ->
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                        Text(state.names[req.uid] ?: req.uid.take(6), modifier = Modifier.weight(1f), maxLines = 1)
                        TextButton(onClick = { onDecide(req.id, true) }, enabled = !state.busy) { Text(stringResource(R.string.room_approve)) }
                        TextButton(onClick = { onDecide(req.id, false) }, enabled = !state.busy) { Text(stringResource(R.string.room_reject)) }
                    }
                }
            }
            MenuRow(stringResource(R.string.room_leave), onClick = onLeaveRoom)
            if (myRole == RoomRole.Owner) {
                MenuRow(stringResource(R.string.room_close), enabled = !state.busy, danger = true, onClick = onClose)
            }
        }
    }
}

@Composable
private fun MenuRow(text: String, enabled: Boolean = true, danger: Boolean = false, onClick: () -> Unit) {
    Text(
        text,
        color = when {
            !enabled -> MaterialTheme.colorScheme.onSurfaceVariant
            danger -> MaterialTheme.colorScheme.error
            else -> MaterialTheme.colorScheme.onSurface
        },
        modifier = Modifier.fillMaxWidth().clickable(enabled = enabled, onClick = onClick).padding(vertical = 14.dp),
    )
}

/** إشعار هدية حيّ أعلى الغرفة؛ يختفي تلقائياً بعد ثوانٍ. */
@Composable
private fun GiftBannerView(
    banner: GiftBanner,
    myUid: String,
    names: Map<String, String>,
    onDismiss: (Long) -> Unit,
    modifier: Modifier = Modifier,
) {
    LaunchedEffect(banner.messageId) {
        kotlinx.coroutines.delay(3_500)
        onDismiss(banner.messageId)
    }
    val from = if (banner.senderUid == myUid) stringResource(R.string.chat_me) else (names[banner.senderUid] ?: banner.senderUid.take(6))
    val to = if (banner.event.recipientUid == myUid) stringResource(R.string.chat_me) else (names[banner.event.recipientUid] ?: banner.event.recipientUid.take(6))
    androidx.compose.animation.AnimatedVisibility(
        visible = true,
        enter = androidx.compose.animation.slideInVertically(initialOffsetY = { -it }) + androidx.compose.animation.fadeIn(),
        modifier = modifier,
    ) {
        Row(
            Modifier
                .safeDrawingPadding()
                .padding(horizontal = 16.dp, vertical = 8.dp)
                .fillMaxWidth()
                .background(
                    VoiceUpColors.Amber,
                    androidx.compose.foundation.shape.RoundedCornerShape(16.dp),
                )
                .padding(horizontal = 14.dp, vertical = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Icon(Icons.Rounded.CardGiftcard, contentDescription = null, tint = VoiceUpColors.OnAmber)
            Text(
                stringResource(R.string.chat_gift, from, to, giftName(banner.event.nameAr, banner.event.nameEn)),
                color = VoiceUpColors.OnAmber,
                style = MaterialTheme.typography.titleSmall,
            )
        }
    }
}
