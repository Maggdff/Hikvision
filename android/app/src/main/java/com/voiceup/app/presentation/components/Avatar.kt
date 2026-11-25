package com.voiceup.app.presentation.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp

/** (خلفية، حرف): ألوان هادئة متناغمة مع التصميم الداكن. */
private val avatarPalettes = listOf(
    Color(0xFF2B3A32) to Color(0xFFA9C9B5),
    Color(0xFF3A3128) to Color(0xFFD9BC95),
    Color(0xFF2F3440) to Color(0xFFA9B6D1),
    Color(0xFF3B2F36) to Color(0xFFD2AEBF),
    Color(0xFF2E3B3B) to Color(0xFFA5CFCB),
    Color(0xFF38362B) to Color(0xFFCFC9A0),
)

/** صورة رمزية مولَّدة: حرف أول على تدرج ثابت لكل مستخدم (من seed). رفع الصور الحقيقية يأتي لاحقاً. */
@Composable
fun Avatar(name: String, seed: String, size: Dp = 48.dp, modifier: Modifier = Modifier) {
    val (bg, fg) = avatarPalettes[Math.floorMod(seed.hashCode(), avatarPalettes.size)]
    Box(
        modifier = modifier
            .size(size)
            .clip(CircleShape)
            .background(bg),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = name.trim().take(1).uppercase().ifEmpty { "؟" },
            color = fg,
            style = if (size >= 56.dp) MaterialTheme.typography.titleLarge else MaterialTheme.typography.titleMedium,
        )
    }
}
