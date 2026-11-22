import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    // الاختبارات التي تستخدم قاعدة البيانات تعيد إنشاء المخطط، فلا تعمل بالتوازي
    fileParallelism: false,
  },
});
