pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}
dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
        // LiveKit يعتمد على audioswitch المنشورة في JitPack فقط (نقصر المستودع على هذه المجموعة)
        maven("https://jitpack.io") {
            content { includeGroup("com.github.davidliu") }
        }
    }
}

rootProject.name = "VoiceUp"
include(":app")
