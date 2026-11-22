package com.voiceup.app.presentation.shell

import androidx.annotation.StringRes
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ExitToApp
import androidx.compose.material.icons.filled.ChatBubble
import androidx.compose.material.icons.filled.Explore
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.Person
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.NavigationBarItemDefaults
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.voiceup.app.R
import com.voiceup.app.domain.model.UserProfile
import com.voiceup.app.presentation.components.Avatar
import com.voiceup.app.presentation.theme.VoiceUpColors

enum class MainTab(@StringRes val label: Int, val icon: ImageVector) {
    Home(R.string.tab_home, Icons.Filled.Home),
    Discover(R.string.tab_discover, Icons.Filled.Explore),
    Messages(R.string.tab_messages, Icons.Filled.ChatBubble),
    Me(R.string.tab_me, Icons.Filled.Person),
}

/** الهيكل الرئيسي: شريط تنقل سفلي وانتقال متلاشٍ بين الأقسام. */
@Composable
fun MainShell(
    profile: UserProfile,
    tabIndex: Int,
    onTab: (Int) -> Unit,
    onSignOut: () -> Unit,
    home: @Composable () -> Unit,
) {
    Scaffold(
        containerColor = MaterialTheme.colorScheme.background,
        contentWindowInsets = WindowInsets(0, 0, 0, 0),
        bottomBar = {
            NavigationBar(containerColor = VoiceUpColors.Surface) {
                MainTab.values().forEachIndexed { i, tab ->
                    NavigationBarItem(
                        selected = tabIndex == i,
                        onClick = { onTab(i) },
                        icon = { Icon(tab.icon, contentDescription = null) },
                        label = { Text(stringResource(tab.label)) },
                        colors = NavigationBarItemDefaults.colors(
                            selectedIconColor = Color.White,
                            selectedTextColor = VoiceUpColors.Violet,
                            indicatorColor = VoiceUpColors.Violet,
                        ),
                    )
                }
            }
        },
    ) { inner ->
        Box(Modifier.fillMaxSize().padding(inner)) {
            AnimatedContent(
                targetState = tabIndex,
                transitionSpec = { fadeIn() togetherWith fadeOut() },
                label = "tabs",
            ) { index ->
                when (MainTab.values()[index]) {
                    MainTab.Home -> home()
                    MainTab.Discover -> ComingSoon(Icons.Filled.Explore, R.string.discover_soon_body)
                    MainTab.Messages -> ComingSoon(Icons.Filled.ChatBubble, R.string.messages_soon_body)
                    MainTab.Me -> ProfileTab(profile, onSignOut)
                }
            }
        }
    }
}

@Composable
private fun ComingSoon(icon: ImageVector, @StringRes body: Int) {
    Column(
        Modifier.fillMaxSize().background(VoiceUpColors.BackgroundGradient).statusBarsPadding().padding(32.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(12.dp, Alignment.CenterVertically),
    ) {
        Box(
            Modifier.size(96.dp).clip(CircleShape).background(VoiceUpColors.AccentGradient),
            contentAlignment = Alignment.Center,
        ) {
            Icon(icon, contentDescription = null, tint = Color.White, modifier = Modifier.size(44.dp))
        }
        Text(stringResource(R.string.soon_title), style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
        Text(
            stringResource(body),
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
        )
    }
}

@Composable
private fun ProfileTab(profile: UserProfile, onSignOut: () -> Unit) {
    Column(
        Modifier.fillMaxSize().background(VoiceUpColors.BackgroundGradient).statusBarsPadding().padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Spacer(Modifier.height(24.dp))
        Avatar(profile.displayName.ifBlank { profile.username }, profile.uid, 104.dp)
        Spacer(Modifier.height(12.dp))
        Text(
            profile.displayName.ifBlank { profile.username },
            style = MaterialTheme.typography.headlineSmall,
            fontWeight = FontWeight.Bold,
        )
        Text(profile.username, color = MaterialTheme.colorScheme.onSurfaceVariant)
        if (profile.bio.isNotBlank()) {
            Spacer(Modifier.height(8.dp))
            Text(profile.bio, textAlign = TextAlign.Center)
        }
        Spacer(Modifier.height(20.dp))
        Surface(shape = RoundedCornerShape(20.dp), color = MaterialTheme.colorScheme.surfaceVariant) {
            Row(Modifier.fillMaxWidth().padding(vertical = 14.dp), horizontalArrangement = Arrangement.SpaceEvenly) {
                Stat("0", R.string.me_followers)
                Stat("0", R.string.me_following)
                Stat("1", R.string.me_level)
            }
        }
        Spacer(Modifier.weight(1f))
        OutlinedButton(onClick = onSignOut, modifier = Modifier.fillMaxWidth()) {
            Icon(Icons.AutoMirrored.Filled.ExitToApp, contentDescription = null, modifier = Modifier.size(18.dp))
            Text(stringResource(R.string.sign_out), modifier = Modifier.padding(start = 8.dp))
        }
    }
}

@Composable
private fun Stat(value: String, @StringRes label: Int) {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Text(value, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
        Text(stringResource(label), style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}
