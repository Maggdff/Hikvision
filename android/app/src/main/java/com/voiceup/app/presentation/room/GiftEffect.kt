package com.voiceup.app.presentation.room

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Icon
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.text.drawText
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.sin

/** درجة التأثير؛ تُشتق من سعر الهدية (القيم من الخادم) فلا يلزم تحميل الكتالوج. */
enum class EffectLevel { Small, Medium, Large }

/**
 * العتبات هنا للعرض فقط (طول الحركة)، ولا علاقة لها بأي حساب مالي.
 * أصغر من 200 عملة: خفيف، أقل من 1000: متوسط، وغير ذلك: مشهد كامل.
 */
fun effectLevelFor(priceCoins: Int): EffectLevel = when {
    priceCoins >= 1_000 -> EffectLevel.Large
    priceCoins >= 200 -> EffectLevel.Medium
    else -> EffectLevel.Small
}

fun effectDurationMillis(level: EffectLevel): Int = when (level) {
    EffectLevel.Small -> 2_200
    EffectLevel.Medium -> 3_000
    EffectLevel.Large -> 4_600
}

private val Cyan = Color(0xFF4FC3FF)
private val Blue = Color(0xFF2A5BFF)
private val Violet = Color(0xFF7A3DFF)
private val Amber = Color(0xFFF4A93B)

/**
 * تأثير الهدية بملء منطقة الغرفة: يُرسم فوق المقاعد والدردشة ولا يلتقط اللمس.
 *  - صغير: الأيقونة تصعد مع شرارات.
 *  - متوسط: توهج نابض وأيقونة تظهر بحجم أكبر مع انفجار نقاط ضوء.
 *  - كبير (مثل السيارة والصاروخ): مشهد من أربع مراحل: ظهور بتوهج، حركة عبر الشاشة بخطوط سرعة،
 *    عدّادات سرعة تصل إلى 200، ثم نفق ضوئي يبتلع المشهد.
 * كله رسم أصلي بالـCanvas بلا ملفات فيديو.
 */
@Composable
fun GiftEffect(
    effectKey: Long,
    giftId: String,
    priceCoins: Int,
    modifier: Modifier = Modifier,
    onFinished: () -> Unit,
) {
    val level = effectLevelFor(priceCoins)
    val progress = remember(effectKey) { Animatable(0f) }
    LaunchedEffect(effectKey) {
        progress.snapTo(0f)
        progress.animateTo(1f, tween(effectDurationMillis(level), easing = LinearEasing))
        onFinished()
    }
    val p = progress.value
    val icon = giftIcon(giftId)
    val measurer = rememberTextMeasurer()

    Box(modifier.fillMaxSize()) {
        when (level) {
            EffectLevel.Small -> {
                Canvas(Modifier.fillMaxSize()) { sparkles(p, Amber, count = 14, spread = 0.5f) }
                Icon(
                    icon, contentDescription = null, tint = Amber,
                    modifier = Modifier
                        .align(Alignment.Center)
                        .size(72.dp)
                        .graphicsLayer {
                            translationY = size.height * (1.6f - 3.4f * p)
                            val s = 0.7f + 0.5f * (1f - kotlin.math.abs(2 * p - 1))
                            scaleX = s; scaleY = s
                        }
                        .alpha(if (p > 0.85f) (1f - p) / 0.15f else 1f),
                )
            }
            EffectLevel.Medium -> {
                Canvas(Modifier.fillMaxSize()) {
                    val a = fade(p, 0.12f, 0.8f)
                    drawRect(Brush.radialGradient(listOf(Violet.copy(alpha = 0.45f * a), Color.Transparent), center, size.minDimension))
                    drawCircle(Amber.copy(alpha = 0.18f * a), radius = size.minDimension * (0.25f + 0.25f * p), center = center)
                    sparkles(p, Color.White, count = 26, spread = 1f)
                }
                val pop = easeOutBack((p / 0.3f).coerceIn(0f, 1f))
                Icon(
                    icon, contentDescription = null, tint = Amber,
                    modifier = Modifier
                        .align(Alignment.Center)
                        .size(150.dp)
                        .graphicsLayer { scaleX = pop; scaleY = pop; rotationZ = -8f + 16f * p }
                        .alpha(fade(p, 0.1f, 0.8f)),
                )
            }
            EffectLevel.Large -> LargeScene(p, icon, giftId, measurer)
        }
    }
}

@Composable
private fun LargeScene(
    p: Float,
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    giftId: String,
    measurer: androidx.compose.ui.text.TextMeasurer,
) {
    // مراحل: [0,.25] ظهور • [.25,.55] حركة • [.55,.8] عدّادات • [.8,1] نفق
    val showCar = p < 0.8f
    val speedometer = giftId == "car" || giftId == "rocket"
    Box(Modifier.fillMaxSize()) {
        Canvas(Modifier.fillMaxSize()) {
            // خلفية زرقاء داكنة شفافة تتوهج تحت المشهد
            val bg = fade(p, 0.06f, 0.9f)
            drawRect(Brush.verticalGradient(listOf(Color(0xFF0B1C5C).copy(alpha = 0.55f * bg), Color(0xFF2A0F66).copy(alpha = 0.35f * bg))))
            drawRect(Brush.radialGradient(listOf(Cyan.copy(alpha = 0.35f * bg), Color.Transparent), center, size.maxDimension * 0.6f))
            if (p in 0.25f..0.8f) speedLines(((p - 0.25f) / 0.55f), Cyan)
            if (speedometer && p in 0.52f..0.82f) gauges(((p - 0.52f) / 0.30f).coerceIn(0f, 1f), measurer)
            if (p >= 0.78f) tunnel(((p - 0.78f) / 0.22f).coerceIn(0f, 1f))
        }
        if (showCar) {
            // ظهور بتوهج ثم عبور من اليسار لليمين مع ميل خفيف
            val appear = easeOutBack((p / 0.25f).coerceIn(0f, 1f))
            val move = ((p - 0.25f) / 0.30f).coerceIn(0f, 1f)
            Icon(
                icon, contentDescription = null, tint = Color(0xFFCFE6FF),
                modifier = Modifier
                    .align(Alignment.Center)
                    .size(180.dp)
                    .graphicsLayer {
                        val s = appear * (1f + 0.35f * move)
                        scaleX = s; scaleY = s
                        translationX = size.width * (-0.15f + 1.6f * move * move)
                        rotationZ = -6f * move
                    }
                    .alpha(if (p > 0.7f) ((0.8f - p) / 0.1f).coerceIn(0f, 1f) else fade(p, 0.05f, 1f)),
            )
        }
    }
}

// ---------- رسم مساعد ----------

private fun fade(p: Float, inEnd: Float, outStart: Float): Float = when {
    p < inEnd -> p / inEnd
    p > outStart -> ((1f - p) / (1f - outStart)).coerceIn(0f, 1f)
    else -> 1f
}

private fun easeOutBack(t: Float): Float {
    val c1 = 1.70158f
    val c3 = c1 + 1f
    val x = t - 1f
    return 1f + c3 * x * x * x + c1 * x * x
}

/** نقاط ضوء تنتشر من المركز؛ مواضعها ثابتة (مشتقة من الفهرس) فلا عشوائية بين الإطارات. */
private fun DrawScope.sparkles(p: Float, color: Color, count: Int, spread: Float) {
    val c = center
    for (i in 0 until count) {
        val ang = (i * 137.5f) * (PI.toFloat() / 180f)
        val speed = 0.35f + 0.65f * (((i * 53) % 100) / 100f)
        val dist = size.minDimension * 0.55f * spread * speed * p
        val x = c.x + cos(ang) * dist
        val y = c.y + sin(ang) * dist - size.minDimension * 0.1f * p
        val a = (1f - p).coerceIn(0f, 1f)
        drawCircle(color.copy(alpha = a), radius = 3f + 5f * speed, center = Offset(x, y))
    }
}

/** خطوط سرعة أفقية تعبر الشاشة. */
private fun DrawScope.speedLines(t: Float, color: Color) {
    for (i in 0 until 16) {
        val y = size.height * (0.15f + 0.7f * ((i * 37) % 100) / 100f)
        val len = size.width * (0.25f + 0.5f * ((i * 61) % 100) / 100f)
        val x = size.width * (1.2f - 1.6f * ((t + i * 0.07f) % 1f))
        drawLine(
            Brush.horizontalGradient(listOf(Color.Transparent, color.copy(alpha = 0.7f)), startX = x, endX = x + len),
            Offset(x, y), Offset(x + len, y), strokeWidth = 2f + (i % 3) * 1.5f, cap = StrokeCap.Round,
        )
    }
}

/** عدّادان: الأيسر أزرق والأيمن أحمر/وردي، والأرقام تصعد حتى 200. */
private fun DrawScope.gauges(t: Float, measurer: androidx.compose.ui.text.TextMeasurer) {
    val r = size.width * 0.22f
    val cy = size.height * 0.5f
    val speed = (200f * t).toInt()
    fun gauge(cx: Float, color: Color, value: Float) {
        drawCircle(color.copy(alpha = 0.12f), r, Offset(cx, cy))
        drawArc(color.copy(alpha = 0.25f), 135f, 270f, false, Offset(cx - r, cy - r), Size(r * 2, r * 2), style = Stroke(10f, cap = StrokeCap.Round))
        drawArc(color, 135f, 270f * value, false, Offset(cx - r, cy - r), Size(r * 2, r * 2), style = Stroke(10f, cap = StrokeCap.Round))
        drawCircle(color.copy(alpha = 0.5f), r * 1.06f, Offset(cx, cy), style = Stroke(2f))
    }
    gauge(size.width * 0.27f, Cyan, t)
    gauge(size.width * 0.73f, Color(0xFFFF4D7A), t)
    val txt = measurer.measure(speed.toString(), TextStyle(color = Color.White, fontSize = 34.sp))
    drawText(txt, topLeft = Offset(size.width * 0.5f - txt.size.width / 2f, cy - txt.size.height / 2f))
}

/** نفق ضوئي: خطوط تتجمع نحو نقطة التلاشي ثم تنطفئ. */
private fun DrawScope.tunnel(t: Float) {
    val c = Offset(size.width / 2f, size.height * 0.48f)
    val a = (1f - t).coerceIn(0f, 1f)
    for (i in 0 until 22) {
        val ang = (i * 360f / 22f) * (PI.toFloat() / 180f)
        val from = size.maxDimension * (0.9f - 0.8f * t)
        val to = from * 0.35f
        drawLine(
            Cyan.copy(alpha = 0.8f * a),
            Offset(c.x + cos(ang) * from, c.y + sin(ang) * from),
            Offset(c.x + cos(ang) * to, c.y + sin(ang) * to),
            strokeWidth = 3f, cap = StrokeCap.Round,
        )
    }
    drawCircle(Color.White.copy(alpha = 0.35f * a), radius = size.minDimension * 0.08f * (1f + t), center = c)
}
