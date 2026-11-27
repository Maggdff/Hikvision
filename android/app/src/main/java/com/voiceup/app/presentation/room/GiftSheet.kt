package com.voiceup.app.presentation.room

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.CardGiftcard
import androidx.compose.material.icons.rounded.Castle
import androidx.compose.material.icons.rounded.Coffee
import androidx.compose.material.icons.rounded.DirectionsCar
import androidx.compose.material.icons.rounded.Favorite
import androidx.compose.material.icons.rounded.LocalFlorist
import androidx.compose.material.icons.rounded.RocketLaunch
import androidx.compose.material.icons.rounded.Star
import androidx.compose.material.icons.rounded.WorkspacePremium
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.voiceup.app.R
import com.voiceup.app.domain.model.Gift
import com.voiceup.app.domain.model.SeatInfo
import com.voiceup.app.presentation.components.Avatar
import com.voiceup.app.presentation.errorMessageRes
import com.voiceup.app.presentation.giftName
import com.voiceup.app.presentation.theme.VoiceUpColors

/**
 * نافذة إرسال هدية تجميلية. لا ميزة تنافسية ولا عشوائية: السعر ثابت ومعروف قبل الإرسال،
 * والمستلم يختاره المرسل من الجالسين على المنصة، ولا يُخصم شيء قبل ضغط «إرسال».
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun GiftSheet(
    state: RoomUiState,
    myUid: String,
    seats: List<SeatInfo>,
    onLoad: () -> Unit,
    onSend: (giftId: String, recipientUid: String) -> Unit,
    onDismiss: () -> Unit,
) {
    LaunchedEffect(Unit) { onLoad() }

    val recipients = seats.filter { it.uid != myUid }
    var chosen by rememberSaveable { mutableStateOf<String?>(null) }
    var selectedGift by rememberSaveable { mutableStateOf<String?>(null) }
    // المستلم المختار قد يغادر المنصة: نرجع لأول متاح
    val recipient = recipients.firstOrNull { it.uid == chosen }?.uid ?: recipients.firstOrNull()?.uid
    val balance = state.balance
    val gift = state.gifts.firstOrNull { it.id == selectedGift }
    val canSend = recipient != null && gift != null && !state.giftSending && balance != null && balance >= gift.priceCoins

    ModalBottomSheet(onDismissRequest = onDismiss, containerColor = MaterialTheme.colorScheme.surface) {
        Column(Modifier.padding(horizontal = 14.dp).padding(bottom = 20.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            // المستلمون: صور دائرية، والمحدد بحلقة عنبرية
            if (recipients.isEmpty()) {
                Text(stringResource(R.string.gift_no_recipients), color = MaterialTheme.colorScheme.onSurfaceVariant)
            } else {
                Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    recipients.forEach { s ->
                        val name = state.names[s.uid] ?: s.uid.take(6)
                        val on = s.uid == recipient
                        Box(
                            Modifier
                                .size(44.dp)
                                .clip(CircleShape)
                                .border(if (on) 2.5.dp else 0.dp, VoiceUpColors.Amber, CircleShape)
                                .clickable { chosen = s.uid },
                            contentAlignment = Alignment.Center,
                        ) { Avatar(name = name, seed = s.uid, size = 44.dp) }
                    }
                }
            }

            // تبويب «الهدايا» (القائم الوحيد حالياً)
            Column {
                Text(
                    stringResource(R.string.gift_title),
                    fontWeight = FontWeight.SemiBold, fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurface,
                    modifier = Modifier.padding(bottom = 6.dp),
                )
                Box(Modifier.height(2.dp).fillMaxWidth(0.16f).background(VoiceUpColors.Amber))
                Box(Modifier.height(1.dp).fillMaxWidth().background(MaterialTheme.colorScheme.outline))
            }

            state.giftError?.let {
                Text(stringResource(errorMessageRes(it)), color = MaterialTheme.colorScheme.error, fontSize = 12.sp)
            }
            if (balance == 0L) {
                Text(stringResource(R.string.gift_no_coins), color = MaterialTheme.colorScheme.onSurfaceVariant, fontSize = 12.sp)
            }

            Box(Modifier.fillMaxWidth().height(250.dp), contentAlignment = Alignment.Center) {
                when {
                    state.giftLoading && state.gifts.isEmpty() -> CircularProgressIndicator(color = VoiceUpColors.Amber)
                    state.gifts.isEmpty() -> Text(stringResource(R.string.gift_empty), color = MaterialTheme.colorScheme.onSurfaceVariant)
                    else -> LazyVerticalGrid(
                        columns = GridCells.Fixed(4),
                        verticalArrangement = Arrangement.spacedBy(10.dp),
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        items(state.gifts, key = { it.id }) { g ->
                            val affordable = balance != null && balance >= g.priceCoins
                            GiftCell(g, selected = g.id == selectedGift, dimmed = !affordable) { selectedGift = g.id }
                        }
                    }
                }
            }

            // الرصيد وزر الإرسال
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Row(
                    Modifier
                        .clip(RoundedCornerShape(50))
                        .border(1.dp, MaterialTheme.colorScheme.outline, RoundedCornerShape(50))
                        .padding(horizontal = 16.dp, vertical = 14.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    Image(painterResource(R.drawable.ic_coin), contentDescription = null, modifier = Modifier.size(18.dp))
                    Text(if (balance != null) "$balance" else "…", fontWeight = FontWeight.SemiBold)
                }
                Box(
                    Modifier
                        .weight(1f)
                        .clip(RoundedCornerShape(50))
                        .background(VoiceUpColors.Amber)
                        .alpha(if (canSend) 1f else 0.4f)
                        .clickable(enabled = canSend) {
                            if (recipient != null && gift != null) onSend(gift.id, recipient)
                        }
                        .padding(vertical = 15.dp),
                    contentAlignment = Alignment.Center,
                ) {
                    if (state.giftSending) {
                        CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp, color = VoiceUpColors.OnAmber)
                    } else {
                        Text(stringResource(R.string.gift_send), color = VoiceUpColors.OnAmber, fontWeight = FontWeight.Bold, fontSize = 15.sp)
                    }
                }
            }
        }
    }
}

internal fun giftIcon(id: String): ImageVector = when (id) {
    "rose" -> Icons.Rounded.LocalFlorist
    "heart" -> Icons.Rounded.Favorite
    "coffee" -> Icons.Rounded.Coffee
    "star" -> Icons.Rounded.Star
    "crown" -> Icons.Rounded.WorkspacePremium
    "rocket" -> Icons.Rounded.RocketLaunch
    "car" -> Icons.Rounded.DirectionsCar
    "palace" -> Icons.Rounded.Castle
    else -> Icons.Rounded.CardGiftcard
}

@Composable
private fun GiftCell(gift: Gift, selected: Boolean, dimmed: Boolean, onClick: () -> Unit) {
    Column(
        Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(14.dp))
            .border(if (selected) 1.5.dp else 0.dp, if (selected) VoiceUpColors.Amber else MaterialTheme.colorScheme.surface, RoundedCornerShape(14.dp))
            .clickable(onClick = onClick)
            .padding(vertical = 10.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(5.dp),
    ) {
        Box(
            Modifier.size(52.dp).clip(RoundedCornerShape(14.dp)).background(MaterialTheme.colorScheme.surfaceVariant),
            contentAlignment = Alignment.Center,
        ) {
            Icon(giftIcon(gift.id), contentDescription = null, tint = VoiceUpColors.Amber, modifier = Modifier.size(26.dp))
        }
        Text(
            giftName(gift.nameAr, gift.nameEn),
            fontSize = 11.5.sp, textAlign = TextAlign.Center, maxLines = 1,
            color = MaterialTheme.colorScheme.onSurface.copy(alpha = if (dimmed) 0.5f else 1f),
        )
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(3.dp)) {
            Image(painterResource(R.drawable.ic_coin), contentDescription = null, modifier = Modifier.size(11.dp))
            Text("${gift.priceCoins}", fontSize = 11.sp, color = VoiceUpColors.Amber.copy(alpha = if (dimmed) 0.5f else 1f))
        }
    }
}
