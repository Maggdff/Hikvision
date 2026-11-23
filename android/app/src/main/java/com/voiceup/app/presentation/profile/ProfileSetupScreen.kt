package com.voiceup.app.presentation.profile

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.voiceup.app.R
import com.voiceup.app.presentation.theme.appBackground

@Composable
fun ProfileSetupScreen(
    state: ProfileSetupState,
    onUsername: (String) -> Unit,
    onDisplayName: (String) -> Unit,
    onBio: (String) -> Unit,
    onDay: (String) -> Unit,
    onMonth: (String) -> Unit,
    onYear: (String) -> Unit,
    onSubmit: () -> Unit,
    onSignOut: () -> Unit,
) {
    Column(
        Modifier
            .fillMaxSize()
            .background(appBackground())
            .safeDrawingPadding()
            .verticalScroll(rememberScrollState())
            .padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        if (state.underAge) {
            Text(stringResource(R.string.profile_underage_title), style = MaterialTheme.typography.titleLarge)
            Text(
                stringResource(R.string.profile_underage_body),
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Start,
            )
            Button(onClick = onSignOut, modifier = Modifier.fillMaxWidth()) {
                Text(stringResource(R.string.sign_out))
            }
            return@Column
        }

        Text(stringResource(R.string.profile_title), style = MaterialTheme.typography.headlineSmall)

        OutlinedTextField(
            value = state.username,
            onValueChange = onUsername,
            label = { Text(stringResource(R.string.profile_username)) },
            supportingText = { Text(stringResource(R.string.profile_username_hint)) },
            isError = state.error == ProfileFormError.Username || state.error == ProfileFormError.UsernameTaken,
            singleLine = true,
            modifier = Modifier.fillMaxWidth(),
        )
        OutlinedTextField(
            value = state.displayName,
            onValueChange = onDisplayName,
            label = { Text(stringResource(R.string.profile_display_name)) },
            isError = state.error == ProfileFormError.DisplayName,
            singleLine = true,
            modifier = Modifier.fillMaxWidth(),
        )
        OutlinedTextField(
            value = state.bio,
            onValueChange = onBio,
            label = { Text(stringResource(R.string.profile_bio)) },
            supportingText = { Text("${state.bio.length}/160") },
            modifier = Modifier.fillMaxWidth(),
        )

        Text(stringResource(R.string.profile_birth_title), style = MaterialTheme.typography.titleMedium)
        Text(
            stringResource(R.string.profile_birth_note),
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            val birthError = state.error == ProfileFormError.BirthDate
            OutlinedTextField(
                value = state.day, onValueChange = onDay,
                label = { Text(stringResource(R.string.profile_day)) },
                isError = birthError, singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                modifier = Modifier.weight(1f),
            )
            OutlinedTextField(
                value = state.month, onValueChange = onMonth,
                label = { Text(stringResource(R.string.profile_month)) },
                isError = birthError, singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                modifier = Modifier.weight(1f),
            )
            OutlinedTextField(
                value = state.year, onValueChange = onYear,
                label = { Text(stringResource(R.string.profile_year)) },
                isError = birthError, singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                modifier = Modifier.weight(1.4f),
            )
        }

        state.error?.let { Text(stringResource(errorRes(it)), color = MaterialTheme.colorScheme.error) }

        Button(onClick = onSubmit, enabled = !state.loading, modifier = Modifier.fillMaxWidth()) {
            if (state.loading) CircularProgressIndicator(Modifier.padding(2.dp), strokeWidth = 2.dp)
            else Text(stringResource(R.string.profile_save))
        }
        TextButton(onClick = onSignOut) { Text(stringResource(R.string.sign_out)) }
    }
}

private fun errorRes(e: ProfileFormError): Int = when (e) {
    ProfileFormError.Username -> R.string.profile_error_username
    ProfileFormError.DisplayName -> R.string.profile_error_display_name
    ProfileFormError.Bio -> R.string.profile_error_bio
    ProfileFormError.BirthDate -> R.string.profile_error_birth
    ProfileFormError.UsernameTaken -> R.string.profile_error_taken
    ProfileFormError.Generic -> R.string.profile_error_generic
}
