/// إعدادات تُمرَّر وقت البناء (--dart-define). لا أسرار هنا.
class Config {
  static const apiUrl = String.fromEnvironment('VOICEUP_API_URL');
  static const fbApiKey = String.fromEnvironment('FB_API_KEY');
  static const fbAppId = String.fromEnvironment('FB_APP_ID');
  static const fbSenderId = String.fromEnvironment('FB_SENDER_ID');
  static const fbProjectId = String.fromEnvironment('FB_PROJECT_ID');
  static const fbStorageBucket = String.fromEnvironment('FB_STORAGE_BUCKET');

  static bool get apiConfigured => apiUrl.isNotEmpty;
  static bool get firebaseConfigured => fbApiKey.isNotEmpty && fbAppId.isNotEmpty && fbProjectId.isNotEmpty;
}
