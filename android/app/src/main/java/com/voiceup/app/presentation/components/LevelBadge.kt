package com.voiceup.app.presentation.components

import androidx.annotation.DrawableRes
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.size
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.voiceup.app.R

enum class LevelKind { Wealth, Charm }

/** شكل الشارة يتدرج على 10 فئات: 1–19، 20–39، 40–49، 50–59، 60–69، 70–79، 80–89، 90–99، 100–125، 126–150. */
fun levelTier(level: Int): Int = when {
    level < 20 -> 1
    level < 40 -> 2
    level < 50 -> 3
    level < 60 -> 4
    level < 70 -> 5
    level < 80 -> 6
    level < 90 -> 7
    level < 100 -> 8
    level < 126 -> 9
    else -> 10
}

@DrawableRes
fun levelBadgeRes(kind: LevelKind, level: Int): Int {
    val t = levelTier(level)
    return if (kind == LevelKind.Wealth) when (t) {
        1 -> R.drawable.lvl_wealth_1; 2 -> R.drawable.lvl_wealth_2; 3 -> R.drawable.lvl_wealth_3
        4 -> R.drawable.lvl_wealth_4; 5 -> R.drawable.lvl_wealth_5; 6 -> R.drawable.lvl_wealth_6
        7 -> R.drawable.lvl_wealth_7; 8 -> R.drawable.lvl_wealth_8; 9 -> R.drawable.lvl_wealth_9
        else -> R.drawable.lvl_wealth_10
    } else when (t) {
        1 -> R.drawable.lvl_charm_1; 2 -> R.drawable.lvl_charm_2; 3 -> R.drawable.lvl_charm_3
        4 -> R.drawable.lvl_charm_4; 5 -> R.drawable.lvl_charm_5; 6 -> R.drawable.lvl_charm_6
        7 -> R.drawable.lvl_charm_7; 8 -> R.drawable.lvl_charm_8; 9 -> R.drawable.lvl_charm_9
        else -> R.drawable.lvl_charm_10
    }
}

/** شارة المستوى مع رقمه. */
@Composable
fun LevelBadge(kind: LevelKind, level: Int, size: Dp = 40.dp, modifier: Modifier = Modifier) {
    Row(modifier, verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        Image(painter = painterResource(levelBadgeRes(kind, level)), contentDescription = null, modifier = Modifier.size(size))
        Text("LV $level", style = MaterialTheme.typography.labelLarge)
    }
}
