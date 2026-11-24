package com.voiceup.app.data.api

import com.voiceup.app.domain.model.ApiException
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import java.io.IOException
import java.util.concurrent.TimeUnit

@Serializable
private data class ErrorDto(val error: String = "unknown")

internal fun defaultHttpClient(): OkHttpClient = OkHttpClient.Builder()
    .connectTimeout(10, TimeUnit.SECONDS)
    .callTimeout(20, TimeUnit.SECONDS)
    .build()

/** عميل HTTP مشترك: يضيف Firebase ID Token ويحوّل أخطاء الشبكة والخادم إلى ApiException. */
internal class ApiClient(
    private val baseUrl: String,
    private val client: OkHttpClient,
    private val tokens: suspend () -> String,
) {
    val json = Json { ignoreUnknownKeys = true }
    private val jsonType = "application/json; charset=utf-8".toMediaType()

    suspend fun <T> call(
        method: String,
        path: String,
        body: String? = null,
        headers: Map<String, String> = emptyMap(),
        parse: (String) -> T,
    ): T = withContext(Dispatchers.IO) {
        if (baseUrl.isBlank()) throw ApiException(0, "not_configured")
        val token = try {
            tokens()
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            throw ApiException(401, "unauthenticated")
        }
        // GET بلا جسم، وغيره بجسم JSON (OkHttp يرفض POST/PUT بلا جسم)
        val requestBody = if (method == "GET") null else (body ?: "{}").toRequestBody(jsonType)
        val request = Request.Builder()
            .url(baseUrl.trimEnd('/') + "/v1" + path)
            .header("Authorization", "Bearer $token")
            .apply { headers.forEach { (k, v) -> header(k, v) } }
            .method(method, requestBody)
            .build()
        try {
            client.newCall(request).execute().use { resp ->
                val text = resp.body?.string().orEmpty()
                if (!resp.isSuccessful) {
                    val code = runCatching { json.decodeFromString<ErrorDto>(text).error }.getOrDefault("unknown")
                    throw ApiException(resp.code, code)
                }
                parse(text)
            }
        } catch (e: IOException) {
            throw ApiException(0, "network")
        } catch (e: kotlinx.serialization.SerializationException) {
            throw ApiException(0, "bad_response")
        }
    }
}
