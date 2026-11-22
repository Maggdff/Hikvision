package com.voiceup.app.presentation.components

import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.requiredSize
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.voiceup.app.R
import com.voiceup.app.presentation.theme.VoiceUpColors

private val palette = listOf(
    Color(0xFF7C3AED) to Color(0xFF2563EB),
    Color(0xFFDB2777) to Color(0xFF7C3AED),
    Color(0xFF0891B2) to Color(0xFF4F46E5),
    Color(0xFFEA580C) to Color(0xFFDB2777),
    Color(0xFF059669) to Color(0xFF0891B2),
)

/** تدرج لوني ثابت لكل مُعرّف (غرفة أو مستخدم) حتى يبقى لونه نفسه في كل مرة. */
fun gradientFor(seed: String): Brush {
    val (a, b) = palette[(seed.hashCode() and Int.MAX_VALUE) % palette.size]
    return Brush.linearGradient(listOf(a, b))
}

/** صورة رمزية دائرية بالحرف الأول، وحلقات موجية تتسع حولها عندما يتحدث صاحبها. */
@Composable
fun Avatar(
    name: String,
    seed: String,
    diameter: Dp,
    modifier: Modifier = Modifier,
    speaking: Boolean = false,
) {
    Box(modifier.size(diameter), contentAlignment = Alignment.Center) {
        if (speaking) WaveRings(diameter)
        Box(
            Modifier
                .size(diameter)
                .clip(CircleShape)
                .background(gradientFor(seed))
                .border(
                    width = if (speaking) 3.dp else 1.dp,
                    color = if (speaking) VoiceUpColors.Violet else Color.White.copy(alpha = 0.15f),
                    shape = CircleShape,
                ),
            contentAlignment = Alignment.Center,
        ) {
            Text(
                text = name.trim().take(1).uppercase(),
                color = Color.White,
                fontWeight = FontWeight.Bold,
                fontSize = (diameter.value * 0.4f).sp,
            )
        }
    }
}

@Composable
private fun WaveRings(diameter: Dp) {
    val transition = rememberInfiniteTransition(label = "wave")
    val progress by transition.animateFloat(
        initialValue = 0f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(tween(1500, easing = LinearEasing)),
        label = "waveProgress",
    )
    Canvas(Modifier.requiredSize(diameter * 1.6f)) {
        val center = Offset(this.size.width / 2f, this.size.height / 2f)
        val base = diameter.toPx() / 2f
        for (i in 0..1) {
            val p = (progress + i * 0.5f) % 1f
            drawCircle(
                color = VoiceUpColors.Violet.copy(alpha = 0.6f * (1f - p)),
                radius = base * (1f + 0.55f * p),
                center = center,
                style = Stroke(width = 3.dp.toPx()),
            )
        }
    }
}

/** مقعد فارغ. */
@Composable
fun EmptySeat(diameter: Dp, modifier: Modifier = Modifier) {
    Box(
        modifier
            .size(diameter)
            .clip(CircleShape)
            .background(MaterialTheme.colorScheme.surface)
            .border(1.dp, Color.White.copy(alpha = 0.12f), CircleShape),
        contentAlignment = Alignment.Center,
    ) {
        Icon(
            Icons.Filled.Add,
            contentDescription = null,
            tint = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.size(diameter / 3),
        )
    }
}

/** شارة "مباشر" بنقطة نابضة. */
@Composable
fun LiveBadge(modifier: Modifier = Modifier) {
    val transition = rememberInfiniteTransition(label = "live")
    val alpha by transition.animateFloat(
        initialValue = 1f,
        targetValue = 0.25f,
        animationSpec = infiniteRepeatable(tween(800), RepeatMode.Reverse),
        label = "liveAlpha",
    )
    Row(
        modifier
            .clip(RoundedCornerShape(50))
            .background(Color.Black.copy(alpha = 0.28f))
            .padding(horizontal = 10.dp, vertical = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Box(
            Modifier
                .size(8.dp)
                .clip(CircleShape)
                .background(VoiceUpColors.Live.copy(alpha = alpha)),
        )
        Text(
            stringResource(R.string.live),
            color = Color.White,
            style = MaterialTheme.typography.labelMedium,
            fontWeight = FontWeight.Bold,
        )
    }
}

/** بطاقة هيكلية نابضة تُعرض أثناء تحميل القائمة. */
@Composable
fun ShimmerCard(modifier: Modifier = Modifier) {
    val transition = rememberInfiniteTransition(label = "shimmer")
    val alpha by transition.animateFloat(
        initialValue = 0.3f,
        targetValue = 0.7f,
        animationSpec = infiniteRepeatable(tween(900), RepeatMode.Reverse),
        label = "shimmerAlpha",
    )
    Box(
        modifier
            .height(132.dp)
            .clip(RoundedCornerShape(24.dp))
            .background(VoiceUpColors.SurfaceHigh.copy(alpha = alpha)),
    )
}

/** صف نقاط يمثل المقاعد: ممتلئة = مشغولة، فارغة = متاحة. */
@Composable
fun SeatDots(seated: Int, max: Int, modifier: Modifier = Modifier) {
    Row(modifier, horizontalArrangement = Arrangement.spacedBy(5.dp), verticalAlignment = Alignment.CenterVertically) {
        for (i in 0 until max) {
            Box(
                Modifier
                    .size(10.dp)
                    .clip(CircleShape)
                    .background(if (i < seated) Color.White else Color.White.copy(alpha = 0.25f)),
            )
        }
        Spacer(Modifier.width(2.dp))
    }
}
