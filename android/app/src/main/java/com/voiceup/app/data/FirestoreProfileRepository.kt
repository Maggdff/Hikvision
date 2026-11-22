package com.voiceup.app.data

import com.google.firebase.firestore.FieldValue
import com.google.firebase.firestore.FirebaseFirestore
import com.voiceup.app.domain.model.ProfileDraft
import com.voiceup.app.domain.model.UserProfile
import com.voiceup.app.domain.model.UsernameTakenException
import com.voiceup.app.domain.repository.ProfileRepository
import com.voiceup.app.domain.validation.Validators
import kotlinx.coroutines.tasks.await

/**
 * users/{uid}                  ← الملف العام
 * users/{uid}/private/profile  ← تاريخ الميلاد (يقرؤه المالك فقط)
 * usernames/{username}         ← حجز اسم المستخدم (تفرد)
 */
class FirestoreProfileRepository(
    private val db: FirebaseFirestore = FirebaseFirestore.getInstance(),
) : ProfileRepository {

    override suspend fun getProfile(uid: String): UserProfile? {
        val doc = db.collection("users").document(uid).get().await()
        if (!doc.exists()) return null
        return UserProfile(
            uid = uid,
            username = doc.getString("username").orEmpty(),
            displayName = doc.getString("displayName").orEmpty(),
            bio = doc.getString("bio").orEmpty(),
            country = doc.getString("country"),
        )
    }

    override suspend fun updateProfile(uid: String, displayName: String, bio: String, country: String?) {
        db.collection("users").document(uid).update(
            mapOf(
                "displayName" to displayName.trim(),
                "bio" to bio.trim(),
                "country" to (country ?: FieldValue.delete()),
            ),
        ).await()
    }

    override suspend fun createProfile(uid: String, draft: ProfileDraft) {
        val username = Validators.normalizeUsername(draft.username)
        val userRef = db.collection("users").document(uid)
        val privateRef = userRef.collection("private").document("profile")
        val nameRef = db.collection("usernames").document(username)
        try {
            db.runTransaction { tx ->
                if (tx.get(nameRef).exists()) throw UsernameTakenException()
                tx.set(nameRef, mapOf("uid" to uid))
                tx.set(
                    userRef,
                    mapOf(
                        "username" to username,
                        "displayName" to draft.displayName.trim(),
                        "bio" to draft.bio.trim(),
                        "createdAt" to FieldValue.serverTimestamp(),
                    ),
                )
                tx.set(privateRef, mapOf("birthDate" to draft.birthDate.toString()))
                null
            }.await()
        } catch (e: Exception) {
            if (e is UsernameTakenException || e.cause is UsernameTakenException) throw UsernameTakenException()
            throw e
        }
    }
}
