package com.voiceup.app.presentation.main

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Home
import androidx.compose.material.icons.rounded.Person
import androidx.compose.material3.Icon
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.res.stringResource
import com.voiceup.app.R
import com.voiceup.app.presentation.theme.VoiceUpTheme

enum class MainTab { Home, Account }

/**
 * التنقل السفلي. يحوي التبويبات العاملة فقط؛ تُضاف تبويبات (استكشاف، رسائل) عند بنائها فعلاً.
 */
@Composable
fun MainScaffold(
    home: @Composable () -> Unit,
    account: @Composable () -> Unit,
) {
    var tab by rememberSaveable { mutableStateOf(MainTab.Home) }
    // الرئيسية داكنة، و«حسابي» كريمية فاتحة كما في التصميم
    VoiceUpTheme(dark = tab == MainTab.Home) {
    Scaffold(
        containerColor = androidx.compose.material3.MaterialTheme.colorScheme.background,
        bottomBar = {
            NavigationBar(
                containerColor = androidx.compose.material3.MaterialTheme.colorScheme.surface,
            ) {
                tabItem(MainTab.Home, tab, R.string.nav_home, Icons.Rounded.Home) { tab = it }
                tabItem(MainTab.Account, tab, R.string.nav_account, Icons.Rounded.Person) { tab = it }
            }
        },
    ) { padding ->
        Box(Modifier.fillMaxSize().padding(padding)) {
            when (tab) {
                MainTab.Home -> home()
                MainTab.Account -> account()
            }
        }
    }
    }
}

@Composable
private fun androidx.compose.foundation.layout.RowScope.tabItem(
    me: MainTab,
    current: MainTab,
    label: Int,
    icon: ImageVector,
    onSelect: (MainTab) -> Unit,
) {
    val selected = me == current
    NavigationBarItem(
        selected = selected,
        onClick = { onSelect(me) },
        icon = { Icon(icon, contentDescription = null) },
        label = { Text(stringResource(label)) },
        alwaysShowLabel = true,
        colors = androidx.compose.material3.NavigationBarItemDefaults.colors(
            selectedIconColor = androidx.compose.material3.MaterialTheme.colorScheme.primary,
            selectedTextColor = androidx.compose.material3.MaterialTheme.colorScheme.primary,
            indicatorColor = androidx.compose.ui.graphics.Color.Transparent,
            unselectedIconColor = androidx.compose.material3.MaterialTheme.colorScheme.onSurfaceVariant,
            unselectedTextColor = androidx.compose.material3.MaterialTheme.colorScheme.onSurfaceVariant,
        ),
    )
}
