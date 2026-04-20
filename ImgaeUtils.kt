package com.ppp.pwcattendence

import android.graphics.Bitmap
import android.util.Base64
import java.io.ByteArrayOutputStream

fun Bitmap.toBase64(): String {
    val outputStream = ByteArrayOutputStream()
    this.compress(Bitmap.CompressFormat.JPEG, 70, outputStream)
    val byteArray = outputStream.toByteArray()
    
    // 🔥 CHANGED FROM Base64.DEFAULT TO Base64.NO_WRAP
    return "data:image/jpeg:base64," + Base64.encodeToString(byteArray, Base64.NO_WRAP)
}