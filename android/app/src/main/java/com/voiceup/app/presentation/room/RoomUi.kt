package com.voiceup.app.presentation.room

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.Send
import androidx.compose.material.icons.rounded.CardGiftcard
import androidx.compose.material.icons.rounded.GridView
import androidx.compose.material.icons.rounded.Mic
import androidx.compose.material.icons.rounded.MicOff
import androidx.compose.material.icons.rounded.Person
import androidx.compose.material.icons.rounded.PowerSettingsNew
import androidx.compose.material.icons.rounded.Schedule
import androidx.compose.material.icons.rounded.WorkspacePremium
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.voiceup.app.R
import com.voiceup.app.domain.model.ChatMessage
import com.voiceup.app.domain.model.SeatInfo
import com.voiceup.app.domain.voice.VoiceState
import com.voiceup.app.presentation.components.Avatar
import com.voiceup.app.presentation.giftName
import com.voiceup.app.presentation.theme.VoiceUpColors
import java.time.Instant
import java.util.Locale

/** صيغة المدة HH:MM:SS؛ ثوانٍ سالبة (فرق ساعات الأجهزة) تُعامل صفراً. */
fun formatElapsed(totalSeconds: Long): String {
    val s = totalSeconds.coerceAtLeast(0)
    return String.format(Locale.US, "%02d:%02d:%02d", s / 3600, (s / 60) % 60, s % 60)
}

/** أرقام بفواصل (1,240) بصيغة لاتينية ثابتة. */
fun formatCount(n: Long): String = String.format(Locale.US, "%,d", n)

@Composable
internal fun RoomTopBar(
    title: String,
    seated: Int,
    voice: VoiceState,
    onExit: () -> Unit,
    onReconnect: () -> Unit,
) {
    Row(
        Modifier.fillMaxWidth().padding(horizontal = 14.dp).padding(top = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Box(
            Modifier
                .size(40.dp)
                .clip(RoundedCornerShape(12.dp))
                .background(MaterialTheme.colorScheme.surface)
                .border(1.dp, MaterialTheme.colorScheme.outline, RoundedCornerShape(12.dp))
                .clickable(onClick = onExit),
            contentAlignment = Alignment.Center,
        ) {
            Icon(Icons.Rounded.PowerSettingsNew, contentDescription = stringResource(R.string.room_leave), tint = MaterialTheme.colorScheme.onSurface, modifier = Modifier.size(19.dp))
        }
        Column(Modifier.weight(1f)) {
            Text(title, fontWeight = FontWeight.SemiBold, fontSize = 15.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(3.dp)) {
                    Icon(Icons.Rounded.Person, contentDescription = null, tint = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.size(12.dp))
                    Text("$seated", fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                Text(
                    stringResource(
                        when (voice) {
                            VoiceState.Connecting -> R.string.voice_connecting
                            VoiceState.Connected -> R.string.voice_connected
                            VoiceState.Reconnecting -> R.string.voice_reconnecting
                            VoiceState.Disconnected -> R.string.voice_disconnected
                            VoiceState.Failed -> R.string.voice_failed
                        },
                    ),
                    fontSize = 11.sp,
                    color = if (voice == VoiceState.Connected) VoiceUpColors.Amber else MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
        if (voice == VoiceState.Disconnected || voice == VoiceState.Failed) {
            Text(
                stringResource(R.string.room_reconnect),
                color = VoiceUpColors.OnAmber, fontWeight = FontWeight.SemiBold, fontSize = 12.sp,
                modifier = Modifier
                    .clip(RoundedCornerShape(17.dp))
                    .background(VoiceUpColors.Amber)
                    .clickable(onClick = onReconnect)
                    .padding(horizontal = 16.dp, vertical = 9.dp),
            )
        }
    }
}

@Composable
private fun InfoPill(content: @Composable () -> Unit) {
    Row(
        Modifier
            .clip(RoundedCornerShape(50))
            .border(1.dp, MaterialTheme.colorScheme.outline, RoundedCornerShape(50))
            .padding(horizontal = 10.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(5.dp),
    ) { content() }
}

/** مدة الغرفة (تعدّ كل ثانية من وقت الإنشاء الذي يعطيه الخادم) ومجموع قيمة الهدايا داخلها. */
@Composable
internal fun RoomInfoRow(createdAt: String, giftTotal: Long) {
    val startMillis = remember(createdAt) { runCatching { Instant.parse(createdAt).toEpochMilli() }.getOrNull() }
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(startMillis) {
        while (startMillis != null) {
            now = System.currentTimeMillis()
            kotlinx.coroutines.delay(1_000)
        }
    }
    Row(Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 10.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        if (startMillis != null) {
            InfoPill {
                Icon(Icons.Rounded.Schedule, contentDescription = null, tint = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.size(14.dp))
                Text(formatElapsed((now - startMillis) / 1000), fontSize = 11.5.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
        InfoPill {
            Image(painterResource(R.drawable.ic_coin), contentDescription = null, modifier = Modifier.size(14.dp))
            Text(formatCount(giftTotal), fontSize = 11.5.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable
private fun PointsPill(points: Long) {
    Row(
        Modifier
            .clip(RoundedCornerShape(50))
            .background(MaterialTheme.colorScheme.surface)
            .border(1.dp, MaterialTheme.colorScheme.outline, RoundedCornerShape(50))
            .padding(horizontal = 8.dp, vertical = 2.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(3.dp),
    ) {
        Image(painterResource(R.drawable.ic_coin), contentDescription = null, modifier = Modifier.size(10.dp))
        Text(formatCount(points), fontSize = 10.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

/** مقعد المضيف الكبير: حلقة عنبرية وتاج. */
@Composable
internal fun HostSeat(name: String?, seed: String?, speaking: Boolean, points: Long, onClick: (() -> Unit)?) {
    Column(
        Modifier.let { if (onClick != null) it.clickable(onClick = onClick) else it },
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Icon(Icons.Rounded.WorkspacePremium, contentDescription = null, tint = VoiceUpColors.Amber, modifier = Modifier.size(18.dp))
        Box(
            Modifier
                .size(80.dp)
                .clip(CircleShape)
                .border(if (speaking) 4.dp else 2.dp, VoiceUpColors.Amber, CircleShape),
            contentAlignment = Alignment.Center,
        ) {
            if (name != null) Avatar(name = name, seed = seed ?: name, size = 80.dp)
        }
        Text(
            name ?: stringResource(R.string.seat_empty),
            fontWeight = FontWeight.SemiBold, fontSize = 13.sp, maxLines = 1, overflow = TextOverflow.Ellipsis,
            modifier = Modifier.padding(top = 6.dp),
        )
        PointsPill(points)
    }
}

@Composable
internal fun SeatCell(
    number: Int,
    name: String?,
    seed: String?,
    speaking: Boolean,
    points: Long,
    onClick: (() -> Unit)?,
) {
    val line = MaterialTheme.colorScheme.onSurfaceVariant
    Column(
        Modifier
            .width(82.dp)
            .let { if (onClick != null) it.clickable(onClick = onClick) else it },
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        if (name == null) {
            Box(
                Modifier
                    .size(56.dp)
                    .drawBehind {
                        drawRoundRect(
                            line.copy(alpha = 0.6f), cornerRadius = CornerRadius(size.minDimension / 2),
                            style = Stroke(2f, pathEffect = PathEffect.dashPathEffect(floatArrayOf(8f, 8f))),
                        )
                    },
                contentAlignment = Alignment.Center,
            ) {
                Icon(Icons.Rounded.Mic, contentDescription = null, tint = line, modifier = Modifier.size(20.dp))
            }
            Text("$number", fontSize = 11.sp, color = line, modifier = Modifier.padding(top = 6.dp))
        } else {
            Box(
                Modifier
                    .size(56.dp)
                    .clip(CircleShape)
                    .border(if (speaking) 3.dp else 0.dp, VoiceUpColors.Amber, CircleShape),
                contentAlignment = Alignment.Center,
            ) { Avatar(name = name, seed = seed ?: name, size = 56.dp) }
            Text(name, fontSize = 11.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(top = 5.dp))
            PointsPill(points)
        }
    }
}

/** أسطر الدردشة: رسالة ترحيب من النظام ثم الرسائل والهدايا. */
@Composable
internal fun RoomChat(
    messages: List<ChatMessage>,
    names: Map<String, String>,
    myUid: String,
    modifier: Modifier = Modifier,
) {
    val listState = rememberLazyListState()
    LaunchedEffect(messages.lastOrNull()?.id) {
        if (messages.isNotEmpty()) listState.animateScrollToItem(messages.lastIndex + 1)
    }
    LazyColumn(
        state = listState,
        modifier = modifier.fillMaxWidth().padding(horizontal = 14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Rounded.CardGiftcard, contentDescription = null, tint = VoiceUpColors.Amber, modifier = Modifier.size(14.dp))
                Text(
                    stringResource(R.string.room_welcome),
                    fontSize = 11.5.sp, color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(start = 7.dp),
                )
            }
        }
        items(messages, key = { it.id }) { m ->
            val name = if (m.uid == myUid) stringResource(R.string.chat_me) else (names[m.uid] ?: m.uid.take(6))
            val gift = m.gift
            if (gift != null) {
                val to = if (gift.recipientUid == myUid) stringResource(R.string.chat_me) else (names[gift.recipientUid] ?: gift.recipientUid.take(6))
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    Icon(giftIcon(gift.giftId), contentDescription = null, tint = VoiceUpColors.Amber, modifier = Modifier.size(16.dp))
                    Text(
                        stringResource(R.string.chat_gift, name, to, giftName(gift.nameAr, gift.nameEn)),
                        fontSize = 12.5.sp, color = VoiceUpColors.Amber,
                    )
                }
            } else {
                Column(verticalArrangement = Arrangement.spacedBy(3.dp)) {
                    Text(name, fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Text(
                        m.body,
                        fontSize = 13.sp,
                        modifier = Modifier
                            .clip(RoundedCornerShape(14.dp))
                            .background(MaterialTheme.colorScheme.surface)
                            .border(1.dp, MaterialTheme.colorScheme.outline, RoundedCornerShape(14.dp))
                            .padding(horizontal = 14.dp, vertical = 8.dp),
                    )
                }
            }
        }
    }
}

/** الشريط السفلي: زر الهدية العنبري، حقل الرسالة، الميكروفون (لمن على المنصة)، وقائمة الغرفة. */
@Composable
internal fun RoomBottomBar(
    sending: Boolean,
    onSend: (String, () -> Unit) -> Unit,
    onGift: () -> Unit,
    showMic: Boolean,
    micOn: Boolean,
    onMic: () -> Unit,
    onMenu: () -> Unit,
    menuBadge: Int,
) {
    var draft by rememberSaveable { mutableStateOf("") }
    fun submit() { if (draft.isNotBlank() && !sending) onSend(draft) { draft = "" } }
    Row(
        Modifier
            .fillMaxWidth()
            .height(76.dp)
            .background(MaterialTheme.colorScheme.surface)
            .padding(horizontal = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Box(
            Modifier.size(48.dp).clip(CircleShape).background(VoiceUpColors.Amber).clickable(onClick = onGift),
            contentAlignment = Alignment.Center,
        ) {
            Icon(Icons.Rounded.CardGiftcard, contentDescription = stringResource(R.string.gift_title), tint = VoiceUpColors.OnAmber)
        }
        TextField(
            value = draft,
            onValueChange = { draft = it.take(300) },
            placeholder = { Text(stringResource(R.string.chat_hint), fontSize = 13.sp) },
            singleLine = true,
            shape = RoundedCornerShape(24.dp),
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Send),
            keyboardActions = KeyboardActions(onSend = { submit() }),
            trailingIcon = {
                if (draft.isNotBlank()) {
                    IconButton(onClick = { submit() }, enabled = !sending) {
                        Icon(Icons.AutoMirrored.Rounded.Send, contentDescription = stringResource(R.string.chat_send), tint = VoiceUpColors.Amber)
                    }
                }
            },
            colors = TextFieldDefaults.colors(
                focusedContainerColor = MaterialTheme.colorScheme.surfaceVariant,
                unfocusedContainerColor = MaterialTheme.colorScheme.surfaceVariant,
                focusedIndicatorColor = Color.Transparent,
                unfocusedIndicatorColor = Color.Transparent,
            ),
            modifier = Modifier.weight(1f),
        )
        if (showMic) {
            IconButton(onClick = onMic) {
                Icon(
                    if (micOn) Icons.Rounded.Mic else Icons.Rounded.MicOff,
                    contentDescription = stringResource(if (micOn) R.string.room_mic_on else R.string.room_mic_off),
                    tint = if (micOn) VoiceUpColors.Amber else MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
        Box {
            IconButton(onClick = onMenu) {
                Icon(Icons.Rounded.GridView, contentDescription = stringResource(R.string.room_menu), tint = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            if (menuBadge > 0) {
                Text(
                    "$menuBadge", fontSize = 10.sp, color = Color.White, fontWeight = FontWeight.Bold,
                    modifier = Modifier
                        .align(Alignment.TopEnd)
                        .clip(CircleShape)
                        .background(Color(0xFFE5484D))
                        .padding(horizontal = 5.dp, vertical = 1.dp),
                )
            }
        }
    }
}
