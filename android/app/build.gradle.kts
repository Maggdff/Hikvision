plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.kotlin.serialization)
}

android {
    namespace = "com.voiceup.app"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.voiceup.app"
        minSdk = 26
        targetSdk = 35
        versionCode = 1
        versionName = "0.1.0"

        // عنوان الخادم يأتي من خاصية Gradle أو متغير بيئة (ليس سراً). فارغ = التطبيق يعرض "الخادم غير مُعدّ".
        val apiUrl = (project.findProperty("VOICEUP_API_URL") as String?)
            ?: System.getenv("VOICEUP_API_URL")
            ?: ""
        buildConfigField("String", "API_BASE_URL", "\"$apiUrl\"")

        // وضع تجريبي: بيانات وهمية بلا Firebase ولا خادم (لمعاينة الواجهات). يُفعَّل بـ -PVOICEUP_DEMO=true أو متغير بيئة.
        val demo = ((project.findProperty("VOICEUP_DEMO") as String?) ?: System.getenv("VOICEUP_DEMO") ?: "false")
            .equals("true", ignoreCase = true)
        buildConfigField("boolean", "DEMO_MODE", demo.toString())
    }

    // مفتاح debug ثابت (يأتي من سر GitHub في CI) حتى لا تتغير بصمة SHA-1 المسجلة في Firebase
    val debugKeystore = file("debug.keystore")
    if (debugKeystore.exists()) {
        signingConfigs.getByName("debug") {
            storeFile = debugKeystore
            storePassword = "android"
            keyAlias = "androiddebugkey"
            keyPassword = "android"
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    // اللغات المدعومة: العربية (الافتراضية) والإنجليزية
    // (androidResources.localeFilters يحتاج AGP 8.8+؛ نستخدم resourceConfigurations المتوافق مع 8.7.3)
    defaultConfig { resourceConfigurations += listOf("ar", "en") }
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.lifecycle.runtime.ktx)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.activity.compose)
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.tooling.preview)
    implementation(libs.androidx.compose.material3)
    debugImplementation(libs.androidx.compose.ui.tooling)

    implementation(platform(libs.firebase.bom))
    implementation(libs.firebase.auth)
    implementation(libs.firebase.firestore)
    implementation(libs.kotlinx.coroutines.android)
    implementation(libs.kotlinx.coroutines.play.services)
    implementation(libs.okhttp)
    implementation(libs.kotlinx.serialization.json)
    implementation(libs.livekit.android)

    testImplementation(libs.junit)
    testImplementation(libs.kotlinx.coroutines.test)
    testImplementation(libs.okhttp.mockwebserver)
}

// إضافة Google Services تحتاج google-services.json؛ نطبقها فقط إن وُجد الملف حتى يُبنى الوضع التجريبي بدونه
if (file("google-services.json").exists()) {
    apply(plugin = "com.google.gms.google-services")
}
