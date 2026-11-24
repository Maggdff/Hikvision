package com.voiceup.app.presentation.account

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.voiceup.app.R
import com.voiceup.app.domain.model.Countries
import com.voiceup.app.presentation.components.Avatar
import com.voiceup.app.presentation.countryName
import com.voiceup.app.presentation.home.CountryPickerDialog
import com.voiceup.app.presentation.theme.VoiceUpColors
import com.voiceup.app.presentation.theme.appBackground

@Composable
fun AccountScreen(
    state: AccountUiState,
    onEdit: () -> Unit,
    onCancel: () -> Unit,
    onSave: () -> Unit,
    onDisplayName: (String) -> Unit,
    onBio: (String) -> Unit,
    onCountry: (String?) -> Unit,
    onSignOut: () -> Unit,
) {
    var picking by rememberSaveable { mutableStateOf(false) }
    val p = state.profile

    Column(
        Modifier
            .fillMaxSize()
            .background(appBackground())
            .verticalScroll(rememberScrollState())
            .padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        Text(stringResource(R.string.nav_account), style = MaterialTheme.typography.headlineMedium)

        Row(horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically) {
            Avatar(name = p.displayName, seed = p.uid, size = 64.dp)
            Column {
                Text(p.displayName, style = MaterialTheme.typography.titleLarge)
                Text("@${p.username}", color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }

        // رصيد العملات (للقراءة فقط؛ الشحن عبر Google Play يُضاف لاحقاً)
        Row(
            Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.surface, RoundedCornerShape(16.dp)).padding(16.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(stringResource(R.string.account_coins), style = MaterialTheme.typography.titleMedium)
            Text(
                text = when {
                    state.balance != null -> "${state.balance}"
                    state.balanceFailed -> stringResource(R.string.account_coins_unavailable)
                    else -> "…"
                },
                style = MaterialTheme.typography.titleLarge,
                color = MaterialTheme.colorScheme.primary,
            )
        }

        if (!state.editing) {
            Countries.find(p.country)?.let {
                Text(countryName(it), color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            Text(
                p.bio.ifBlank { stringResource(R.string.account_no_bio) },
                color = if (p.bio.isBlank()) MaterialTheme.colorScheme.onSurfaceVariant else MaterialTheme.colorScheme.onSurface,
            )
            Button(onClick = onEdit, modifier = Modifier.fillMaxWidth()) { Text(stringResource(R.string.account_edit)) }
        } else {
            OutlinedTextField(
                value = state.displayName, onValueChange = onDisplayName,
                label = { Text(stringResource(R.string.profile_display_name)) },
                singleLine = true, modifier = Modifier.fillMaxWidth(),
            )
            OutlinedTextField(
                value = state.bio, onValueChange = onBio,
                label = { Text(stringResource(R.string.profile_bio)) },
                supportingText = { Text("${state.bio.length}/160") },
                modifier = Modifier.fillMaxWidth(),
            )
            OutlinedButton(onClick = { picking = true }, modifier = Modifier.fillMaxWidth()) {
                Text(Countries.find(state.country)?.let { countryName(it) } ?: stringResource(R.string.create_country_none))
            }
            state.errorCode?.let {
                Text(stringResource(accountErrorRes(it)), color = MaterialTheme.colorScheme.error)
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                OutlinedButton(onClick = onCancel, enabled = !state.saving, modifier = Modifier.weight(1f)) {
                    Text(stringResource(R.string.cancel))
                }
                Button(onClick = onSave, enabled = !state.saving, modifier = Modifier.weight(1f)) {
                    if (state.saving) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
                    else Text(stringResource(R.string.account_save))
                }
            }
        }

        TextButton(onClick = onSignOut) { Text(stringResource(R.string.sign_out)) }
    }

    if (picking) {
        CountryPickerDialog(
            selected = state.country,
            allowAll = true,
            onPick = { onCountry(it); picking = false },
            onDismiss = { picking = false },
            noneLabel = R.string.create_country_none,
        )
    }
}

private fun accountErrorRes(code: String): Int = when (code) {
    "invalid_name" -> R.string.account_err_name
    "invalid_bio" -> R.string.account_err_bio
    else -> R.string.account_err_save
}
