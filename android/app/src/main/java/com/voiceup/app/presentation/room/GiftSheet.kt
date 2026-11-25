package com.voiceup.app.presentation.room

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.horizontalScroll
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
import androidx.compose.material3.FilterChip
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.voiceup.app.R
import com.voiceup.app.domain.model.Gift
import com.voiceup.app.domain.model.SeatInfo
import com.voiceup.app.presentation.errorMessageRes
import com.voiceup.app.presentation.giftName

/**
 * نافذة إرسال هدية تجميلية. الهدايا لا تمنح أي ميزة تنافسية ولا عشوائية فيها:
 * السعر ثابت معروف قبل الإرسال، والمستلم يختاره المرسل من الجالسين على المنصة.
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
    // المستلم المختار قد يغادر المنصة: نرجع لأول متاح
    val recipient = recipients.firstOrNull { it.uid == chosen }?.uid ?: recipients.firstOrNull()?.uid
    val balance = state.balance

    ModalBottomSheet(onDismissRequest = onDismiss) {
        Column(Modifier.padding(horizontal = 16.dp).padding(bottom = 24.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                Text(stringResource(R.string.gift_title), style = MaterialTheme.typography.titleLarge)
                Text(
                    if (balance != null) stringResource(R.string.gift_balance, balance) else "…",
                    color = MaterialTheme.colorScheme.primary,
                    style = MaterialTheme.typography.titleMedium,
                )
            }

            if (recipients.isEmpty()) {
                Text(stringResource(R.string.gift_no_recipients), color = MaterialTheme.colorScheme.onSurfaceVariant)
            } else {
                Text(stringResource(R.string.gift_to), style = MaterialTheme.typography.labelLarge)
                Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    recipients.forEach { s ->
                        FilterChip(
                            selected = s.uid == recipient,
                            onClick = { chosen = s.uid },
                            label = { Text(state.names[s.uid] ?: s.uid.take(6)) },
                        )
                    }
                }
            }

            state.giftError?.let {
                Text(stringResource(errorMessageRes(it)), color = MaterialTheme.colorScheme.error)
            }
            if (balance == 0L) {
                Text(stringResource(R.string.gift_no_coins), color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodySmall)
            }

            Box(Modifier.fillMaxWidth().height(250.dp), contentAlignment = Alignment.Center) {
                when {
                    state.giftLoading && state.gifts.isEmpty() -> CircularProgressIndicator()
                    state.gifts.isEmpty() -> Text(stringResource(R.string.gift_empty), color = MaterialTheme.colorScheme.onSurfaceVariant)
                    else -> LazyVerticalGrid(
                        columns = GridCells.Fixed(4),
                        verticalArrangement = Arrangement.spacedBy(10.dp),
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        items(state.gifts, key = { it.id }) { gift ->
                            val affordable = balance != null && balance >= gift.priceCoins
                            val enabled = recipient != null && !state.giftSending && affordable
                            GiftCell(gift, enabled) { if (recipient != null) onSend(gift.id, recipient) }
                        }
                    }
                }
            }
        }
    }
}

private fun giftIcon(id: String): androidx.compose.ui.graphics.vector.ImageVector = when (id) {
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
private fun GiftCell(gift: Gift, enabled: Boolean, onClick: () -> Unit) {
    Column(
        Modifier
            .fillMaxWidth()
            .alpha(if (enabled) 1f else 0.45f)
            .background(MaterialTheme.colorScheme.surfaceVariant, RoundedCornerShape(14.dp))
            .clickable(enabled = enabled, onClick = onClick)
            .padding(vertical = 8.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        Box(
            Modifier.height(44.dp).fillMaxWidth(0.7f).background(MaterialTheme.colorScheme.background, RoundedCornerShape(12.dp)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(giftIcon(gift.id), contentDescription = null, tint = MaterialTheme.colorScheme.primary)
        }
        Text(giftName(gift.nameAr, gift.nameEn), style = MaterialTheme.typography.labelMedium, textAlign = TextAlign.Center, maxLines = 1)
        Text("${gift.priceCoins}", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}
