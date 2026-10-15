// English source messages map to [Uzbek Latin, Russian]. Wire identifiers and
// customer data are never passed through this catalog automatically.
export const messages: Readonly<Record<string, readonly [string, string]>> = {
"Vision": ["Tasvirni ko‘rish","Распознавание изображений"],
"StructuredOutput": ["Tuzilgan chiqish","Структурированный вывод"],
"Reasoning": ["Fikrlash","Рассуждение"],
"Embeddings": ["Vektorlar","Эмбеддинги"],
"ImageGeneration": ["Tasvir yaratish","Генерация изображений"],
"Audio": ["Ovoz","Аудио"],
"disable": ["faolsizlantirish","отключение"],
"enable": ["yoqish","включение"],
"rotate": ["almashtirish","ротация"],
"delete": ["o‘chirish","удаление"],
"Enter a whole UZS amount of at least 1.": ["Kamida 1 UZS butun summani kiriting.","Введите целую сумму UZS не менее 1."],
"Amount exceeds the supported payment limit.": ["Summa qo‘llab-quvvatlanadigan to‘lov limitidan oshdi.","Сумма превышает поддерживаемый платёжный лимит."],
"Request failed ({p0}). Please try again.": ["So‘rov bajarilmadi ({p0}). Qayta urinib ko‘ring.","Ошибка запроса ({p0}). Повторите."],
"Reserved": ["Band qilingan","Зарезервировано"],
"Edit {p0}": ["{p0} ni tahrirlash","Изменить {p0}"],
"Model {p0} is not available under the current project restrictions.": ["Joriy loyiha cheklovlarida {p0} modeli mavjud emas.","Модель {p0} недоступна при текущих ограничениях проекта."],
"No permitted catalog provider for {p0}. A provider pin cannot bypass restrictions.": ["{p0} uchun ruxsatli katalog provayderi yo‘q. Mahkamlash cheklovni chetlab o‘tmaydi.","Нет разрешённого провайдера каталога для {p0}. Фиксация не обходит ограничения."],
"{p0} was credited to your wallet.": ["Hamyonga {p0} tushdi.","В кошелёк зачислено {p0}."],
"{p0} {p1}?": ["{p0}: {p1}?","{p0} {p1}?"],
"Providers": ["Provayderlar","Провайдеры"],
"· subsequently reversed": ["· keyin qaytarib olingan","· впоследствии отменено"],
"e.g. 25.000000": ["masalan, 25.000000","например, 25.000000"],
"Published": ["E’lon qilingan","Опубликовано"],
"Text": ["Matn","Текст"],
"Tools": ["Vositalar","Инструменты"],
"Delivered": ["Yetkazilgan","Доставлено"],
"Suppressed": ["To‘xtatilgan","Подавлено"],
"NotStarted": ["Boshlanmagan","Не начато"],
"Partial": ["Qisman","Частично"],
"ClientDisconnected": ["Mijoz uzilgan","Клиент отключился"],
"PendingAdmission": ["Qabul kutilmoqda","Ожидание допуска"],
"PendingEvidence": ["Dalil kutilmoqda","Ожидание сведений"],
"PendingSettlement": ["Yakunlash kutilmoqda","Ожидание урегулирования"],
"Settled": ["Yakunlangan","Урегулировано"],
"Released": ["Bo‘shatilgan","Освобождено"],
"Estimated": ["Taxminiy","Оценено"],
"Reconciled": ["Hisoblar solishtirilgan","Сверено"],
  "% managed markup. A fixed": [
    "% boshqariladigan ustama. Har so‘rovga belgilangan",
    "% наценки Managed. За каждый запрос добавляется фиксированная плата"
  ],
  "(Archived)": [
    "(Arxivlangan)",
    "(В архиве)"
  ],
  "(UTC)": [
    "(UTC)",
    "(UTC)"
  ],
  "(you)": [
    "(siz)",
    "(вы)"
  ],
  ", only if an attempt slot and eligible rejection permit it.": [
    ", faqat urinish limiti va mos rad etish bunga ruxsat bersa.",
    ", только если лимит попыток и допустимый отказ это позволяют."
  ],
  ". Admins cannot invite, change or revoke Admins/Owners. The last active Owner is protected. Every operation is re-authorized by the API.": [
    ". Adminlar Admin/Egalarni taklif qila, o‘zgartira yoki chiqarib yubora olmaydi. Oxirgi faol Ega himoyalangan. API har amalni qayta tekshiradi.",
    ". Админы не могут приглашать, изменять или отзывать доступ Админов/Владельцев. Последний активный Владелец защищён. API повторно авторизует каждое действие."
  ],
  ". BYOK stays first regardless of managed strategy. No configurable multi-key ordering.": [
    ". Boshqariladigan strategiyadan qat’i nazar, BYOK birinchi. Bir nechta kalit tartibi sozlanmaydi.",
    ". BYOK всегда первый независимо от стратегии Managed. Порядок нескольких ключей не настраивается."
  ],
  ". Credit appears only after a verified provider payment.": [
    ". Kredit faqat tasdiqlangan provayder to‘lovidan keyin tushadi.",
    ". Кредит зачисляется только после подтверждённого платежа провайдера."
  ],
  ". Explicit key only.": [
    ". Faqat aniq tanlangan kalit.",
    ". Только явно выбранный ключ."
  ],
  ". Linking is not confirmed until the destination list says Verified. Closing hides this link; it does not revoke the pending challenge.": [
    ". Ro‘yxatda Tasdiqlangan ko‘rinmaguncha ulanish tasdiqlanmagan. Yopish havolani yashiradi, lekin kutilayotgan tekshiruvni bekor qilmaydi.",
    ". Подключение не подтверждено, пока в списке нет статуса «Подтверждено». Закрытие скрывает ссылку, но не отменяет ожидающую проверку."
  ],
  ". Live eligibility and exact endpoint ranking are not exposed by this API.": [
    ". Bu API joriy yaroqlilik va aniq endpoint reytingini ko‘rsatmaydi.",
    ". Этот API не раскрывает текущую доступность и точный рейтинг endpoint."
  ],
  ". No retroactive capture. If encryption is unavailable, enabling fails closed.": [
    ". Oldingi so‘rovlar saqlanmaydi. Shifrlash ishlamasa, yoqish rad etiladi.",
    ". Прошлые запросы не сохраняются. Если шифрование недоступно, включение отклоняется."
  ],
  ". Other grants are unchanged.": [
    ". Boshqa ruxsatlar o‘zgarmaydi.",
    ". Другие разрешения не изменяются."
  ],
  ". Rotation resets the verification result.": [
    ". Almashtirish tekshiruv natijasini tiklaydi.",
    ". Ротация сбрасывает результат проверки."
  ],
  ". Status uses browser time; acceptance is decided by the API.": [
    ". Holat brauzer vaqti bo‘yicha; qabulni API hal qiladi.",
    ". Статус использует время браузера; принятие определяет API."
  ],
  ". Verification must match the invited account.": [
    ". Tasdiqlangan akkaunt taklifdagi akkauntga mos bo‘lishi kerak.",
    ". Подтверждённый аккаунт должен совпадать с приглашённым."
  ],
  "01 · DESTINATIONS": [
    "01 · MANZILLAR",
    "01 · ПОЛУЧАТЕЛИ"
  ],
  "01 · PROJECT PAYLOAD POLICY": [
    "01 · LOYIHA KONTENTINI SAQLASH",
    "01 · ХРАНЕНИЕ СОДЕРЖИМОГО ПРОЕКТА"
  ],
  "02 · EXPLICIT, AUDITED ACCESS": [
    "02 · ANIQ, AUDITLI KIRISH",
    "02 · ЯВНЫЙ ДОСТУП С АУДИТОМ"
  ],
  "02 · RULES": [
    "02 · QOIDALAR",
    "02 · ПРАВИЛА"
  ],
  "03 · DELIVERY EVIDENCE": [
    "03 · YETKAZISH DALILLARI",
    "03 · СВЕДЕНИЯ О ДОСТАВКЕ"
  ],
  "03 · WHAT THIS DOES NOT DELETE": [
    "03 · NIMALAR O‘CHIRILMAYDI",
    "03 · ЧТО НЕ УДАЛЯЕТСЯ"
  ],
  "; strategy": [
    "; strategiya",
    "; стратегия"
  ],
  "A clear starting point for your first live request.": [
    "Birinchi haqiqiy so‘rovingiz uchun aniq boshlanish nuqtasi.",
    "Понятная отправная точка для первого реального запроса."
  ],
  "A financial recovery balance is outstanding. New managed requests are paused until it is resolved. Contact support with your organization ID.": [
    "Moliyaviy tiklash qarzi bor. U yopilmaguncha yangi boshqariladigan so‘rovlar to‘xtatilgan. Tashkilot ID bilan yordam xizmatiga murojaat qiling.",
    "Есть непогашенная задолженность восстановления. Новые запросы Managed приостановлены до её погашения. Обратитесь в поддержку с ID организации."
  ],
  "A refund here credits an earlier inference charge back to your wallet. It is not a cash payout or payment reversal.": [
    "Bu qaytarish oldingi inferensiya to‘lovini hamyonga qaytaradi. Bu naqd pul yechish yoki to‘lovni bekor qilish emas.",
    "Этот возврат зачисляет ранее списанную стоимость инференса в кошелёк. Это не выплата денег и не отмена платежа."
  ],
  "ACCOUNT ACCESS": [
    "AKKAUNTGA KIRISH",
    "ДОСТУП К АККАУНТУ"
  ],
  "API key": [
    "API kaliti",
    "API-ключ"
  ],
  "API keys": [
    "API kalitlari",
    "API-ключи"
  ],
  "API keys always belong to a project.": [
    "API kalitlari har doim loyihaga tegishli.",
    "API-ключи всегда принадлежат проекту."
  ],
  "API keys could not be loaded for this project.": [
    "Ushbu loyihaning API kalitlari yuklanmadi.",
    "Не удалось загрузить API-ключи проекта."
  ],
  "Accept an invitation": [
    "Taklifni qabul qilish",
    "Принять приглашение"
  ],
  "Accept invitation": [
    "Taklifni qabul qilish",
    "Принять приглашение"
  ],
  "Acceptance happens only when you confirm. Tokens expire after seven days and cannot be replayed. Nothing is stored in browser storage or the query cache.": [
    "Faqat tasdiqlaganingizda qabul qilinadi. Token yetti kunda eskiradi va qayta ishlatilmaydi. Brauzer xotirasi yoki so‘rov keshida hech narsa saqlanmaydi.",
    "Приглашение принимается только после подтверждения. Токен действует семь дней и не используется повторно. Ничего не сохраняется в хранилище браузера или кеше запросов."
  ],
  "Accepted": [
    "Qabul qilingan",
    "Принято"
  ],
  "Account links": [
    "Akkaunt havolalari",
    "Ссылки аккаунта"
  ],
  "Active": [
    "Faol",
    "Активно"
  ],
  "Active request holds": [
    "Faol so‘rov bandlari",
    "Резервы активных запросов"
  ],
  "Activity": [
    "Faoliyat",
    "Активность"
  ],
  "Activity and analytics show real gateway traffic once requests are made. No sample balance or fabricated traffic is shown here.": [
    "So‘rovlar yuborilgach, faoliyat va tahlilda haqiqiy trafik ko‘rinadi. Bu yerda namunaviy balans yoki sun’iy trafik yo‘q.",
    "Активность и аналитика показывают реальный трафик после запросов. Здесь нет примерного баланса или вымышленного трафика."
  ],
  "Activity could not be loaded. Check the filters and try again.": [
    "Faoliyat yuklanmadi. Filtrlarni tekshirib, qayta urinib ko‘ring.",
    "Не удалось загрузить активность. Проверьте фильтры и повторите."
  ],
  "Add provider key": [
    "Provayder kalitini qo‘shish",
    "Добавить ключ провайдера"
  ],
  "Add signed webhook": [
    "Imzolangan webhook qo‘shish",
    "Добавить подписанный webhook"
  ],
  "Add webhook": [
    "Webhook qo‘shish",
    "Добавить webhook"
  ],
  "Adjust filters or ask an operator to publish an active model, mapping, price and managed fee policy.": [
    "Filtrlarni o‘zgartiring yoki operatordan faol model, moslik, narx va boshqariladigan to‘lov siyosatini e’lon qilishni so‘rang.",
    "Измените фильтры или попросите оператора опубликовать активную модель, сопоставление, цену и политику платы Managed."
  ],
  "Admin": [
    "Administrator",
    "Администратор"
  ],
  "Alert access unavailable": [
    "Ogohlantirishlarga kirish yo‘q",
    "Нет доступа к оповещениям"
  ],
  "Alert type": [
    "Ogohlantirish turi",
    "Тип оповещения"
  ],
  "Alerts": [
    "Ogohlantirishlar",
    "Оповещения"
  ],
  "All capabilities": [
    "Barcha imkoniyatlar",
    "Все возможности"
  ],
  "All configured project and key budgets are hard gates. Zero blocks new spend. Windows reset in UTC; holds and captured spend are preserved. No cap-removal operation is available.": [
    "Loyiha va kalitning barcha byudjetlari qat’iy cheklovdir. Nol yangi sarfni bloklaydi. Davrlar UTC bo‘yicha yangilanadi; band va sarflangan mablag‘ saqlanadi. Limitni olib tashlash amali mavjud emas.",
    "Все бюджеты проекта и ключа — обязательные ограничения. Ноль блокирует новые расходы. Периоды обновляются по UTC; резервы и списания сохраняются. Операции удаления лимита нет."
  ],
  "All keys": [
    "Barcha kalitlar",
    "Все ключи"
  ],
  "All models": [
    "Barcha modellar",
    "Все модели"
  ],
  "All projects": [
    "Barcha loyihalar",
    "Все проекты"
  ],
  "All providers": [
    "Barcha provayderlar",
    "Все провайдеры"
  ],
  "All states": [
    "Barcha holatlar",
    "Все состояния"
  ],
  "All types": [
    "Barcha turlar",
    "Все типы"
  ],
  "Allow": [
    "Ruxsat berish",
    "Разрешить"
  ],
  "Allowed canonical models": [
    "Ruxsat etilgan kanonik modellar",
    "Разрешённые канонические модели"
  ],
  "Allowed models": [
    "Ruxsat etilgan modellar",
    "Разрешённые модели"
  ],
  "Allowed providers": [
    "Ruxsat etilgan provayderlar",
    "Разрешённые провайдеры"
  ],
  "Amount (UZS)": [
    "Miqdor (UZS)",
    "Сумма (UZS)"
  ],
  "Amount exceeds the supported Int64 external-spend cap.": [
    "Miqdor qo‘llab-quvvatlanadigan Int64 tashqi sarf limitidan oshdi.",
    "Сумма превышает поддерживаемый предел внешних расходов Int64."
  ],
  "An active webhook already exists. Refresh, then disable it before replacing. No automatic retry.": [
    "Faol webhook mavjud. Yangilang, almashtirishdan oldin uni o‘chiring. Avtomatik qayta urinish yo‘q.",
    "Активный webhook уже существует. Обновите список и отключите его перед заменой. Автоматического повтора нет."
  ],
  "Analytics": [
    "Tahlil",
    "Аналитика"
  ],
  "Analytics could not be loaded. Try a shorter range.": [
    "Tahlil yuklanmadi. Qisqaroq davrni tanlang.",
    "Не удалось загрузить аналитику. Выберите меньший период."
  ],
  "Any context": [
    "Istalgan kontekst",
    "Любой контекст"
  ],
  "Any price": [
    "Istalgan narx",
    "Любая цена"
  ],
  "Applies only to": [
    "Faqat quyidagiga qo‘llanadi",
    "Применяется только к"
  ],
  "Archive": [
    "Arxivlash",
    "Архивировать"
  ],
  "Archive {p0}?": [
    "{p0} arxivlansinmi?",
    "Архивировать {p0}?"
  ],
  "Archived": [
    "Arxivlangan",
    "В архиве"
  ],
  "Archived project": [
    "Arxivlangan loyiha",
    "Архивный проект"
  ],
  "As of": [
    "Ma’lumot vaqti",
    "По состоянию на"
  ],
  "At or above this percentage. 0.01–100%, two decimals.": [
    "Shu foizga teng yoki yuqori. 0.01–100%, ikki kasr xonasi.",
    "Не ниже этого процента. 0.01–100%, два знака после точки."
  ],
  "At or below this amount. Up to six decimals; zero is valid.": [
    "Shu miqdorga teng yoki past. Olti kasr xonasigacha; nol mumkin.",
    "Не выше этой суммы. До шести знаков после точки; ноль допустим."
  ],
  "Auto": [
    "Avtomatik",
    "Авто"
  ],
  "Available balance": [
    "Mavjud balans",
    "Доступный баланс"
  ],
  "Available balance threshold (USD)": [
    "Mavjud balans chegarasi (USD)",
    "Порог доступного баланса (USD)"
  ],
  "BYOK metadata unavailable: only Owner/Admin can inspect credentials. Managed preview remains available. Refresh after access changes.": [
    "BYOK ma’lumotlari mavjud emas: faqat Ega/Administrator ko‘ra oladi. Boshqariladigan ko‘rib chiqish ishlaydi. Kirish o‘zgargach yangilang.",
    "Метаданные BYOK недоступны: их видят только Владелец/Админ. Предпросмотр Managed доступен. Обновите после изменения доступа."
  ],
  "BYOK only (explicit)": [
    "Faqat BYOK (aniq tanlov)",
    "Только BYOK (явно)"
  ],
  "BYOK primary": [
    "Asosiy BYOK",
    "Основной BYOK"
  ],
  "Billing": [
    "Hisob-kitob",
    "Биллинг"
  ],
  "Billing viewer": [
    "Hisob-kitob kuzatuvchisi",
    "Наблюдатель биллинга"
  ],
  "BillingViewer": [
    "Hisob-kitob kuzatuvchisi",
    "Наблюдатель биллинга"
  ],
  "Blank = uncapped; 0 = block new spend": [
    "Bo‘sh = cheklanmagan; 0 = yangi sarfni bloklash",
    "Пусто = без лимита; 0 = блок новых расходов"
  ],
  "Browse models and quickstart": [
    "Modellar va tezkor boshlash",
    "Модели и быстрый старт"
  ],
  "Budget amount (USD)": [
    "Byudjet miqdori (USD)",
    "Сумма бюджета (USD)"
  ],
  "Budget period": [
    "Byudjet davri",
    "Период бюджета"
  ],
  "Budget saved. Existing holds are preserved.": [
    "Byudjet saqlandi. Mavjud bandlar saqlanadi.",
    "Бюджет сохранён. Существующие резервы сохранены."
  ],
  "Budget values exceed exact browser precision. Use an exact-integer API client; no rounded value will be displayed or saved.": [
    "Byudjet brauzerning aniq qiymat chegarasidan oshdi. Aniq sonli API mijozidan foydalaning; yaxlitlangan qiymat ko‘rsatilmaydi yoki saqlanmaydi.",
    "Бюджет превышает точность браузера. Используйте API-клиент с точными целыми числами; округлённое значение не будет показано или сохранено."
  ],
  "Budget warning": [
    "Byudjet ogohlantirishi",
    "Предупреждение бюджета"
  ],
  "BudgetWarning": [
    "Byudjet ogohlantirishi",
    "Предупреждение бюджета"
  ],
  "Build with confidence. Route requests, watch usage, and pay locally.": [
    "Ishonch bilan yarating. So‘rovlarni yo‘naltiring, sarfni kuzating va mahalliy to‘lang.",
    "Создавайте уверенно. Направляйте запросы, следите за расходами и платите локально."
  ],
  "CREDENTIAL CONTROLS ·": [
    "KALIT BOSHQARUVI ·",
    "УПРАВЛЕНИЕ КЛЮЧОМ ·"
  ],
  "CSV downloaded. UTC dates; amounts are integer micro-USD. Import untrusted names as text in spreadsheets.": [
    "CSV yuklandi. Sanalar UTC; summalar butun mikro-USD. Jadval dasturlarida ishonchsiz nomlarni matn sifatida import qiling.",
    "CSV скачан. Даты UTC; суммы в целых микро-USD. Импортируйте недоверенные названия в таблицы как текст."
  ],
  "Cached input": [
    "Keshlangan kirish",
    "Кешированный ввод"
  ],
  "Cancel": [
    "Bekor qilish",
    "Отмена"
  ],
  "Cancel and return to workspace": [
    "Bekor qilish va ish maydoniga qaytish",
    "Отмена и возврат в рабочее пространство"
  ],
  "Canceled": [
    "Bekor qilingan",
    "Отменено"
  ],
  "Candidate sequence — conditional, not a health guarantee": [
    "Nomzodlar ketma-ketligi — shartli, sog‘lomlik kafolati emas",
    "Порядок кандидатов — условный, не гарантия доступности"
  ],
  "Canonical model codes only. Saving replaces this entire group; concurrent edits are last-commit-wins. Budgets are managed separately.": [
    "Faqat kanonik model kodlari. Saqlash butun guruhni almashtiradi; parallel tahrirlarda oxirgi saqlash ustun. Byudjetlar alohida boshqariladi.",
    "Только канонические коды моделей. Сохранение заменяет всю группу; при параллельном редактировании побеждает последняя запись. Бюджеты управляются отдельно."
  ],
  "Capability": [
    "Imkoniyat",
    "Возможность"
  ],
  "Captured": [
    "Sarflangan",
    "Списано"
  ],
  "Captured $": [
    "Sarflangan $",
    "Списано $"
  ],
  "Change access for": [
    "Quyidagi kirishni o‘zgartirish",
    "Изменить доступ для"
  ],
  "Change member role": [
    "A’zo rolini o‘zgartirish",
    "Изменить роль участника"
  ],
  "Change role for": [
    "Quyidagi rolni o‘zgartirish",
    "Изменить роль для"
  ],
  "Charge": [
    "To‘lov",
    "Начисление"
  ],
  "Chat request JSON": [
    "Chat so‘rovi JSON",
    "JSON запроса чата"
  ],
  "Check your details. Passwords must be between 12 and 128 characters.": [
    "Ma’lumotlarni tekshiring. Parol 12–128 belgi bo‘lishi kerak.",
    "Проверьте данные. Пароль должен содержать 12–128 символов."
  ],
  "Checking your session…": [
    "Sessiya tekshirilmoqda…",
    "Проверка сессии…"
  ],
  "Checkout created for": [
    "To‘lov oynasi yaratildi, miqdor",
    "Создана платёжная сессия на"
  ],
  "Checkout unavailable.": [
    "To‘lov oynasi mavjud emas.",
    "Платёжная сессия недоступна."
  ],
  "Choose 1–100 canonical models from this provider, or use Unrestricted. Empty lists are not supported for BYOK.": [
    "Bu provayderdan 1–100 kanonik model tanlang yoki Cheklanmagan ni qo‘llang. BYOK bo‘sh ro‘yxatni qo‘llamaydi.",
    "Выберите 1–100 канонических моделей провайдера или «Без ограничений». Пустые списки BYOK не поддерживаются."
  ],
  "Choose a canonical model": [
    "Kanonik modelni tanlang",
    "Выберите каноническую модель"
  ],
  "Choose a project": [
    "Loyihani tanlang",
    "Выберите проект"
  ],
  "Choose a project first": [
    "Avval loyihani tanlang",
    "Сначала выберите проект"
  ],
  "Choose a published model and copy its chat request": [
    "E’lon qilingan modelni tanlab, chat so‘rovini nusxalang",
    "Выберите опубликованную модель и скопируйте запрос чата"
  ],
  "Choose a valid UTC date range of at most 90 days.": [
    "Ko‘pi bilan 90 kunlik to‘g‘ri UTC davrini tanlang.",
    "Выберите корректный период UTC не более 90 дней."
  ],
  "Choose an active destination": [
    "Faol manzilni tanlang",
    "Выберите активного получателя"
  ],
  "Choose an active project": [
    "Faol loyihani tanlang",
    "Выберите активный проект"
  ],
  "Choose an active provider": [
    "Faol provayderni tanlang",
    "Выберите активного провайдера"
  ],
  "Choose an existing project budget": [
    "Mavjud loyiha byudjetini tanlang",
    "Выберите существующий бюджет проекта"
  ],
  "Choose an explicit credential": [
    "Aniq kalitni tanlang",
    "Выберите конкретный ключ"
  ],
  "Choose the organization in the workspace selector. Developer and Read-only roles still need explicit project grants from an Owner/Admin.": [
    "Tashkilotni ish maydoni tanlovidan tanlang. Dasturchi va Faqat o‘qish rollari uchun Ega/Administratorning aniq loyiha ruxsati kerak.",
    "Выберите организацию в переключателе. Ролям Разработчик и Только чтение нужны явные разрешения на проекты от Владельца/Админа."
  ],
  "Clipboard unavailable. Select and copy the key manually before closing.": [
    "Bufer ishlamayapti. Yopishdan oldin kalitni qo‘lda belgilang va nusxalang.",
    "Буфер недоступен. Выделите и скопируйте ключ вручную перед закрытием."
  ],
  "Close": [
    "Yopish",
    "Закрыть"
  ],
  "Close and clear payload": [
    "Yopish va kontentni tozalash",
    "Закрыть и очистить содержимое"
  ],
  "Close private dialog": [
    "Maxfiy oynani yopish",
    "Закрыть приватное окно"
  ],
  "Compare current provider prices and capabilities, then make your first real chat request.": [
    "Joriy provayder narxlari va imkoniyatlarini solishtiring, keyin birinchi haqiqiy chat so‘rovini yuboring.",
    "Сравните текущие цены и возможности провайдеров, затем отправьте первый реальный запрос чата."
  ],
  "Complete checkout and wait for provider confirmation. No wallet credit has been posted.": [
    "To‘lovni yakunlab, provayder tasdig‘ini kuting. Hamyonga hali kredit yozilmagan.",
    "Завершите оплату и дождитесь подтверждения провайдера. Кредит в кошелёк ещё не зачислен."
  ],
  "Completed": [
    "Yakunlangan",
    "Завершено"
  ],
  "Concurrent requests": [
    "Parallel so‘rovlar",
    "Одновременные запросы"
  ],
  "Configuration example only. No inference is sent. Catalog snapshot:": [
    "Faqat sozlama namunasi. Inferensiya yuborilmaydi. Katalog holati:",
    "Только пример конфигурации. Инференс не отправляется. Снимок каталога:"
  ],
  "Configure": [
    "Sozlash",
    "Настроить"
  ],
  "Configure a project budget on Projects first. This does not create or change spend limits.": [
    "Avval Loyihalar sahifasida byudjet sozlang. Bu sarf limitini yaratmaydi yoki o‘zgartirmaydi.",
    "Сначала настройте бюджет в «Проектах». Это не создаёт и не меняет лимиты расходов."
  ],
  "Configure defaults. Preview explicit requests. Keep hard restrictions in control.": [
    "Standartlarni sozlang. Aniq so‘rovlarni ko‘rib chiqing. Qat’iy cheklovlarni saqlang.",
    "Настройте значения по умолчанию. Проверяйте явные запросы. Сохраняйте обязательные ограничения."
  ],
  "Configure project retention": [
    "Loyiha saqlashini sozlash",
    "Настроить хранение проекта"
  ],
  "Configure retention": [
    "Saqlashni sozlash",
    "Настроить хранение"
  ],
  "Configure {p0}": [
    "{p0} ni sozlash",
    "Настроить {p0}"
  ],
  "Confirm": [
    "Tasdiqlash",
    "Подтвердить"
  ],
  "Confirm archive": [
    "Arxivlashni tasdiqlash",
    "Подтвердить архивацию"
  ],
  "Confirm change": [
    "O‘zgarishni tasdiqlash",
    "Подтвердить изменение"
  ],
  "Confirm disable": [
    "O‘chirishni tasdiqlash",
    "Подтвердить отключение"
  ],
  "Confirm grant change": [
    "Ruxsat o‘zgarishini tasdiqlash",
    "Подтвердить изменение доступа"
  ],
  "Confirm rule change": [
    "Qoida o‘zgarishini tasdiqlash",
    "Подтвердить изменение правила"
  ],
  "Confirm the retention and deletion effects before saving.": [
    "Saqlashdan oldin saqlash va o‘chirish ta’sirini tasdiqlang.",
    "Подтвердите последствия хранения и удаления перед сохранением."
  ],
  "Conflict: an active member/invitation may already exist, the last owner cannot be removed, or this role/grant transition is no longer valid. Refresh before retrying; no automatic retry was made.": [
    "Ziddiyat: faol a’zo/taklif mavjud, oxirgi egani chiqarib bo‘lmaydi yoki rol/ruxsat o‘tishi endi mumkin emas. Qayta urinishdan oldin yangilang; avtomatik urinish bo‘lmadi.",
    "Конфликт: участник/приглашение уже существует, последнего владельца нельзя удалить или переход роли/доступа больше недопустим. Обновите перед повтором; автоматического повтора не было."
  ],
  "Connect Telegram": [
    "Telegram ulash",
    "Подключить Telegram"
  ],
  "Connected credentials": [
    "Ulangan kalitlar",
    "Подключённые ключи"
  ],
  "Context": [
    "Kontekst",
    "Контекст"
  ],
  "Continue to": [
    "Davom etish:",
    "Перейти к"
  ],
  "Copied without an API secret.": [
    "API maxfiy kalitisiz nusxalandi.",
    "Скопировано без секретного API-ключа."
  ],
  "Copy key": [
    "Kalitni nusxalash",
    "Скопировать ключ"
  ],
  "Copy request": [
    "So‘rovni nusxalash",
    "Скопировать запрос"
  ],
  "Copy this key now. Closing this dialog permanently hides the secret. Never put it in browser storage or source control.": [
    "Kalitni hozir nusxalang. Oyna yopilsa maxfiy kalit butunlay yashiriladi. Uni brauzer xotirasi yoki kod omboriga joylamang.",
    "Скопируйте ключ сейчас. Закрытие окна навсегда скрывает секрет. Не храните его в браузере или репозитории."
  ],
  "Create a project": [
    "Loyiha yarating",
    "Создайте проект"
  ],
  "Create a project before managing API keys.": [
    "API kalitlarini boshqarishdan oldin loyiha yarating.",
    "Создайте проект перед управлением API-ключами."
  ],
  "Create a project first": [
    "Avval loyiha yarating",
    "Сначала создайте проект"
  ],
  "Create a workspace, then add a project, top up, and issue your first API key.": [
    "Ish maydoni yarating, keyin loyiha qo‘shing, balans to‘ldiring va birinchi API kalitini oling.",
    "Создайте рабочее пространство, добавьте проект, пополните баланс и выпустите первый API-ключ."
  ],
  "Create alert rule": [
    "Ogohlantirish qoidasini yaratish",
    "Создать правило оповещения"
  ],
  "Create an API key": [
    "API kaliti yaratish",
    "Создать API-ключ"
  ],
  "Create an API key for this project and fund your wallet first. Replace only the placeholder key; never paste a live key into this page.": [
    "Avval loyiha uchun API kaliti yarating va hamyonni to‘ldiring. Faqat kalit o‘rnini almashtiring; haqiqiy kalitni bu sahifaga kiritmang.",
    "Сначала создайте API-ключ проекта и пополните кошелёк. Замените только заглушку ключа; не вставляйте реальный ключ на эту страницу."
  ],
  "Create an account": [
    "Akkaunt yaratish",
    "Создать аккаунт"
  ],
  "Create and securely copy an API key": [
    "API kaliti yarating va xavfsiz nusxalang",
    "Создайте и безопасно скопируйте API-ключ"
  ],
  "Create key": [
    "Kalit yaratish",
    "Создать ключ"
  ],
  "Create organization": [
    "Tashkilot yaratish",
    "Создать организацию"
  ],
  "Create project": [
    "Loyiha yaratish",
    "Создать проект"
  ],
  "Create webhook": [
    "Webhook yaratish",
    "Создать webhook"
  ],
  "Create your account": [
    "Akkauntingizni yarating",
    "Создайте аккаунт"
  ],
  "Created": [
    "Yaratilgan",
    "Создано"
  ],
  "Credential name": [
    "Kalit nomi",
    "Название ключа"
  ],
  "Credit": [
    "Kredit",
    "Зачисление"
  ],
  "Credit pending": [
    "Kredit kutilmoqda",
    "Ожидание зачисления"
  ],
  "Credited": [
    "Kredit tushgan",
    "Зачислено"
  ],
  "Cross-model fallback requires explicit Hybrid managed consent, not BYOK-only execution.": [
    "Modellararo fallback uchun Hybrid boshqariladigan roziligi kerak, faqat BYOK rejimi emas.",
    "Для межмодельного fallback требуется явное согласие Hybrid на Managed, а не режим только BYOK."
  ],
  "Current project": [
    "Joriy loyiha",
    "Текущий проект"
  ],
  "Customer charge": [
    "Mijoz to‘lovi",
    "Начисление клиенту"
  ],
  "Daily": [
    "Kunlik",
    "Ежедневно"
  ],
  "Default routing": [
    "Standart yo‘naltirish",
    "Маршрутизация по умолчанию"
  ],
  "Default-safe": [
    "Xavfsiz standart",
    "Безопасно по умолчанию"
  ],
  "Delete": [
    "O‘chirish",
    "Удалить"
  ],
  "Delivery": [
    "Yetkazish",
    "Доставка"
  ],
  "Delivery destination": [
    "Yetkazish manzili",
    "Получатель"
  ],
  "Denied model codes": [
    "Taqiqlangan model kodlari",
    "Запрещённые коды моделей"
  ],
  "Denied provider codes": [
    "Taqiqlangan provayder kodlari",
    "Запрещённые коды провайдеров"
  ],
  "Deny all (empty list)": [
    "Barchasini taqiqlash (bo‘sh ro‘yxat)",
    "Запретить всё (пустой список)"
  ],
  "Deny wins over allow. Both key and project policies must permit a route, including fallback. Updates affect future admissions, not active streams.": [
    "Taqiq ruxsatdan ustun. Kalit va loyiha fallback ham qo‘shilgan yo‘nalishga ruxsat berishi shart. Yangilash faol oqimga emas, keyingi qabulga ta’sir qiladi.",
    "Запрет важнее разрешения. Политики ключа и проекта должны разрешать маршрут, включая fallback. Изменения влияют на новые допуски, не на активные потоки."
  ],
  "Destination disabled.": [
    "Manzil o‘chirildi.",
    "Получатель отключён."
  ],
  "Destination unavailable": [
    "Manzil mavjud emas",
    "Получатель недоступен"
  ],
  "Deterministic priority": [
    "Belgilangan ustuvorlik",
    "Детерминированный приоритет"
  ],
  "Developer": [
    "Dasturchi",
    "Разработчик"
  ],
  "Developer and Read-only start with no project grants. Billing viewer reads billing/usage but cannot spend or change budgets. Owner cannot be granted by invitation.": [
    "Dasturchi va Faqat o‘qish dastlab loyiha ruxsatisiz. Hisob-kitob kuzatuvchisi hisob/foydalanishni ko‘radi, sarflay yoki byudjet o‘zgartira olmaydi. Taklif orqali Ega berilmaydi.",
    "Разработчик и Только чтение начинают без доступа к проектам. Наблюдатель биллинга читает биллинг/использование, но не расходует и не меняет бюджеты. Владельца нельзя назначить приглашением."
  ],
  "Diagnostics and operational metadata follow a separately approved schedule; financial/audit records remain on legal hold. Existing backups may contain old payload bytes until their approved expiry. Restores must stay quarantined until scrubbed. These are not customer-configurable deletion promises.": [
    "Diagnostika va operatsion ma’lumotlar alohida tasdiqlangan jadvalga amal qiladi; moliyaviy/audit yozuvlari huquqiy saqlashda qoladi. Eski kontent tasdiqlangan muddati tugaguncha zaxirada bo‘lishi mumkin. Tiklangan nusxa tozalanmaguncha karantinda. Bular mijoz sozlaydigan o‘chirish va’dalari emas.",
    "Диагностика и операционные метаданные следуют отдельному утверждённому графику; финансовые/аудиторские записи сохраняются по правовым требованиям. Старое содержимое может оставаться в резервных копиях до их срока удаления. Восстановленные копии изолируются до очистки. Это не настраиваемые клиентом обещания удаления."
  ],
  "Disable": [
    "Faolsizlantirish",
    "Отключить"
  ],
  "Disable {p0}?": [
    "{p0} faolsizlantirilsinmi?",
    "Отключить {p0}?"
  ],
  "Disabled": [
    "O‘chirilgan",
    "Отключено"
  ],
  "Disabled · delete stored project payloads": [
    "O‘chirilgan · saqlangan loyiha kontentini o‘chirish",
    "Отключено · удалить сохранённое содержимое проекта"
  ],
  "Disabled. Editing/testing/new grants unavailable. Existing grants may be removed; deletion is still available.": [
    "O‘chirilgan. Tahrir/tekshiruv/yangi ruxsatlar yo‘q. Mavjud ruxsatni olib tashlash va o‘chirish mumkin.",
    "Отключено. Редактирование/проверка/новые разрешения недоступны. Существующие разрешения можно удалить; удаление ключа доступно."
  ],
  "Dispatched": [
    "Yuborilgan",
    "Отправлено"
  ],
  "Duration": [
    "Davomiylik",
    "Длительность"
  ],
  "Edit": [
    "Tahrirlash",
    "Изменить"
  ],
  "Effective": [
    "Amalda",
    "Действует"
  ],
  "Effective strategy:": [
    "Amaldagi strategiya:",
    "Действующая стратегия:"
  ],
  "Email": [
    "Elektron pochta",
    "Электронная почта"
  ],
  "Email or password is incorrect, or this account is not yet verified.": [
    "Pochta yoki parol xato, yoki akkaunt hali tasdiqlanmagan.",
    "Неверная почта или пароль, либо аккаунт ещё не подтверждён."
  ],
  "Email verified. You can now sign in.": [
    "Pochta tasdiqlandi. Endi kirishingiz mumkin.",
    "Почта подтверждена. Теперь можно войти."
  ],
  "Email, role or status": [
    "Pochta, rol yoki holat",
    "Почта, роль или статус"
  ],
  "Enable": [
    "Yoqish",
    "Включить"
  ],
  "Enable retention explicitly before capturing new requests. Old bodies cannot be reconstructed.": [
    "Yangi so‘rovlarni saqlashdan avval saqlashni aniq yoqing. Eski kontentni tiklab bo‘lmaydi.",
    "Явно включите хранение перед новыми запросами. Старое содержимое восстановить невозможно."
  ],
  "Enable rule?": [
    "Qoida yoqilsinmi?",
    "Включить правило?"
  ],
  "Enabled": [
    "Yoqilgan",
    "Включено"
  ],
  "Enabled · explicit encrypted opt-in": [
    "Yoqilgan · aniq rozilik bilan shifrlash",
    "Включено · явное согласие на шифрование"
  ],
  "Enter a non-negative USD amount with at most 6 decimal places. No exponent or commas.": [
    "Ko‘pi bilan 6 kasr xonali manfiy bo‘lmagan USD kiriting. Eksponent yoki vergulsiz.",
    "Введите неотрицательную сумму USD до 6 знаков после точки. Без экспоненты или запятых."
  ],
  "Enter a valid request UUID from this project’s Activity.": [
    "Shu loyiha Faoliyatidan to‘g‘ri so‘rov UUID kiriting.",
    "Введите корректный UUID запроса из Активности этого проекта."
  ],
  "Enter at least one selected code, or explicitly choose Deny all.": [
    "Kamida bitta kod kiriting yoki aniq Barchasini taqiqlash ni tanlang.",
    "Введите хотя бы один код или явно выберите «Запретить всё»."
  ],
  "Enter verification token": [
    "Tasdiqlash tokenini kiriting",
    "Введите токен подтверждения"
  ],
  "Entire organization": [
    "Butun tashkilot",
    "Вся организация"
  ],
  "Environment not tagged": [
    "Muhit belgilanmagan",
    "Среда не указана"
  ],
  "Environment tag": [
    "Muhit yorlig‘i",
    "Метка среды"
  ],
  "Error rate": [
    "Xatolar ulushi",
    "Доля ошибок"
  ],
  "Error spike": [
    "Xatolar ko‘payishi",
    "Всплеск ошибок"
  ],
  "ErrorSpike": [
    "Xatolar ko‘payishi",
    "Всплеск ошибок"
  ],
  "Estimated external spend": [
    "Taxminiy tashqi sarf",
    "Оценка внешних расходов"
  ],
  "Estimated external spend plus holds has reached the lifetime cap. Gateway admission remains authoritative.": [
    "Taxminiy tashqi sarf va bandlar umumiy limitga yetdi. Gateway qabul qarorini hal qiladi.",
    "Оценка внешних расходов и резервы достигли общего лимита. Решение о допуске принимает Gateway."
  ],
  "Event": [
    "Hodisa",
    "Событие"
  ],
  "Every logical gateway request, its attempts, usage evidence, and final charge.": [
    "Har bir mantiqiy gateway so‘rovi, urinishlari, foydalanish dalili va yakuniy to‘lovi.",
    "Каждый логический запрос gateway, его попытки, сведения об использовании и итоговое списание."
  ],
  "Every model.": [
    "Har bir model.",
    "Каждая модель."
  ],
  "Exact request ID": [
    "Aniq so‘rov ID",
    "Точный ID запроса"
  ],
  "Execution": [
    "Bajarish",
    "Выполнение"
  ],
  "Execution / delivery": [
    "Bajarish / yetkazish",
    "Выполнение / доставка"
  ],
  "Execution mode": [
    "Bajarish rejimi",
    "Режим выполнения"
  ],
  "Execution preview": [
    "Bajarishni ko‘rib chiqish",
    "Предпросмотр выполнения"
  ],
  "Existing financial holds are not erased and BYOK requests do not silently switch to Managed.": [
    "Mavjud moliyaviy bandlar o‘chmaydi va BYOK so‘rovlari yashirincha boshqariladigan rejimga o‘tmaydi.",
    "Существующие финансовые резервы не удаляются, а запросы BYOK не переключаются скрыто на Managed."
  ],
  "Expired": [
    "Muddati tugagan",
    "Истекло"
  ],
  "Expires": [
    "Amal muddati",
    "Истекает"
  ],
  "Expires {p0} (UTC)": [
    "Muddati {p0} (UTC)",
    "Истекает {p0} (UTC)"
  ],
  "Expiry (UTC, optional)": [
    "Amal muddati (UTC, ixtiyoriy)",
    "Срок действия (UTC, необязательно)"
  ],
  "Expiry must be a future UTC date and time.": [
    "Amal muddati kelajakdagi UTC sana va vaqt bo‘lishi kerak.",
    "Срок действия должен быть будущими датой и временем UTC."
  ],
  "Explicit opt-in": [
    "Aniq rozilik",
    "Явное согласие"
  ],
  "Explicit project access": [
    "Aniq loyiha ruxsati",
    "Явный доступ к проекту"
  ],
  "Export activity CSV": [
    "Faoliyat CSV eksporti",
    "Экспорт активности в CSV"
  ],
  "Export could not be downloaded. Try a smaller range and retry.": [
    "Eksport yuklanmadi. Kichikroq davrni tanlab qayta urinib ko‘ring.",
    "Не удалось скачать экспорт. Сократите период и повторите."
  ],
  "Export daily CSV": [
    "Kunlik CSV eksporti",
    "Экспорт по дням в CSV"
  ],
  "Export exceeds 1,000 rows or 4 MiB. Narrow the date range or filters; no partial CSV was downloaded.": [
    "Eksport 1 000 qator yoki 4 MiB dan oshadi. Davr yoki filtrlarni toraytiring; qisman CSV yuklanmadi.",
    "Экспорт превышает 1 000 строк или 4 MiB. Сузьте период или фильтры; частичный CSV не скачан."
  ],
  "Export is not authorized for this scope. Check your session and project access.": [
    "Bu doira uchun eksportga ruxsat yo‘q. Sessiya va loyiha ruxsatini tekshiring.",
    "Экспорт не разрешён для этой области. Проверьте сессию и доступ к проекту."
  ],
  "External provider spend": [
    "Tashqi provayder sarfi",
    "Внешние расходы провайдера"
  ],
  "External reserved": [
    "Tashqi band",
    "Внешний резерв"
  ],
  "Failed": [
    "Xato",
    "Ошибка"
  ],
  "Fallback models must be unique and different from the primary model.": [
    "Fallback modellari takrorlanmasin va asosiy modeldan farq qilsin.",
    "Модели fallback должны быть уникальны и отличаться от основной."
  ],
  "Fee": [
    "Komissiya",
    "Комиссия"
  ],
  "Filter project": [
    "Loyiha filtri",
    "Фильтр проекта"
  ],
  "Financial": [
    "Moliyaviy",
    "Финансы"
  ],
  "Financial budgets use their own UTC windows; analytics never replaces authoritative admission checks.": [
    "Moliyaviy byudjetlar o‘z UTC davrlariga ega; tahlil haqiqiy qabul tekshiruvini almashtirmaydi.",
    "Финансовые бюджеты используют свои периоды UTC; аналитика не заменяет обязательную проверку допуска."
  ],
  "Find": [
    "Qidirish",
    "Найти"
  ],
  "First fallback model": [
    "Birinchi fallback modeli",
    "Первая модель fallback"
  ],
  "Fixed official endpoints only. The secret stays masked, is cleared on submit/close, and cannot be retrieved. Re-enter it after a failed write; refresh first if the outcome is uncertain.": [
    "Faqat rasmiy endpointlar. Maxfiy kalit niqoblangan, yuborish/yopishda tozalanadi va qaytarib olinmaydi. Xato yozishdan keyin qayta kiriting; natija noaniq bo‘lsa avval yangilang.",
    "Только официальные endpoint. Секрет скрыт, очищается при отправке/закрытии и не возвращается. После неудачной записи введите снова; при неопределённом результате сначала обновите."
  ],
  "Forgot password?": [
    "Parolni unutdingizmi?",
    "Забыли пароль?"
  ],
  "From (UTC)": [
    "Boshlanish (UTC)",
    "С (UTC)"
  ],
  "Future request bodies will be stored encrypted until expiry. Shortening also reduces existing expiry times.": [
    "Keyingi so‘rov kontenti muddatigacha shifrlanib saqlanadi. Davrni qisqartirish mavjud yozuvlar muddatini ham qisqartiradi.",
    "Будущее содержимое запросов хранится зашифрованным до истечения срока. Сокращение срока уменьшает срок и существующих записей."
  ],
  "GROW YOUR TEAM": [
    "JAMOANGIZNI KENGAYTIRING",
    "РАСШИРЯЙТЕ КОМАНДУ"
  ],
  "Generate a one-use, 10-minute private-chat link. Only the configured bot can verify your destination; never enter a chat ID here.": [
    "Bir martalik, 10 daqiqalik shaxsiy chat havolasini yarating. Faqat sozlangan bot manzilni tasdiqlaydi; bu yerga chat ID kiritmang.",
    "Создайте одноразовую ссылку на личный чат на 10 минут. Только настроенный бот может подтвердить получателя; не вводите здесь ID чата."
  ],
  "Generate private-chat link": [
    "Shaxsiy chat havolasini yaratish",
    "Создать ссылку на личный чат"
  ],
  "Get quote": [
    "Hisob taklifini olish",
    "Получить расчёт"
  ],
  "Get started": [
    "Boshlash",
    "Начать"
  ],
  "Go home": [
    "Bosh sahifaga",
    "На главную"
  ],
  "Go to projects": [
    "Loyihalarga o‘tish",
    "Перейти к проектам"
  ],
  "Go to sign in": [
    "Kirishga o‘tish",
    "Перейти ко входу"
  ],
  "Grant project access": [
    "Loyihaga ruxsat berish",
    "Предоставить доступ к проекту"
  ],
  "Grant project access?": [
    "Loyihaga ruxsat berilsinmi?",
    "Предоставить доступ к проекту?"
  ],
  "HTTP status": [
    "HTTP holati",
    "HTTP-статус"
  ],
  "Hard restriction. Deny all blocks every route in this dimension.": [
    "Qat’iy cheklov. Barchasini taqiqlash shu o‘lchamdagi barcha yo‘nalishlarni bloklaydi.",
    "Обязательное ограничение. «Запретить всё» блокирует все маршруты этого измерения."
  ],
  "Have an invitation? Accept it here": [
    "Taklifingiz bormi? Shu yerda qabul qiling",
    "Есть приглашение? Примите его здесь"
  ],
  "Hello": [
    "Salom",
    "Привет"
  ],
  "Hello from UZLLM": [
    "UZLLM dan salom",
    "Привет от UZLLM"
  ],
  "Highest throughput": [
    "Eng yuqori o‘tkazuvchanlik",
    "Наибольшая пропускная способность"
  ],
  "Hybrid (explicit opt-in)": [
    "Hybrid (aniq rozilik)",
    "Hybrid (явное согласие)"
  ],
  "Hybrid requires explicit managed-fallback consent.": [
    "Hybrid boshqariladigan fallback uchun aniq rozilikni talab qiladi.",
    "Hybrid требует явного согласия на fallback Managed."
  ],
  "I explicitly allow managed fallback and its full wallet reservation.": [
    "Boshqariladigan fallback va to‘liq hamyon bandiga aniq roziman.",
    "Я явно разрешаю fallback Managed и полный резерв в кошельке."
  ],
  "I saved it": [
    "Saqlab oldim",
    "Я сохранил"
  ],
  "I understand this project’s storage and deletion effects.": [
    "Ushbu loyihaning saqlash va o‘chirish ta’sirini tushunaman.",
    "Я понимаю последствия хранения и удаления для этого проекта."
  ],
  "INFERENCE AUDIT": [
    "INFERENSIYA AUDITI",
    "АУДИТ ИНФЕРЕНСА"
  ],
  "If an account exists for this email, we sent password reset instructions. Check your inbox.": [
    "Bu pochta uchun akkaunt mavjud bo‘lsa, parol tiklash ko‘rsatmasi yuborildi. Pochtangizni tekshiring.",
    "Если аккаунт с этой почтой существует, мы отправили инструкции по сбросу пароля. Проверьте почту."
  ],
  "If this address can be registered, we sent a verification token. Check your email, then verify before signing in.": [
    "Bu manzil ro‘yxatdan o‘ta olsa, tasdiqlash tokeni yuborildi. Pochtani tekshiring va kirishdan avval tasdiqlang.",
    "Если этот адрес может быть зарегистрирован, мы отправили токен подтверждения. Проверьте почту и подтвердите её перед входом."
  ],
  "Independent retention classes": [
    "Mustaqil saqlash toifalari",
    "Независимые классы хранения"
  ],
  "Inherit": [
    "Meros olish",
    "Наследовать"
  ],
  "Inherit deterministic priority": [
    "Belgilangan ustuvorlikni meros olish",
    "Наследовать детерминированный приоритет"
  ],
  "Inherit project (": [
    "Loyihadan meros olish (",
    "Наследовать проект ("
  ],
  "Initial aggregation pending.": [
    "Dastlabki jamlash kutilmoqda.",
    "Ожидается первая агрегация."
  ],
  "Input": [
    "Kirish",
    "Ввод"
  ],
  "Input / output": [
    "Kirish / chiqish",
    "Ввод / вывод"
  ],
  "Inspect a retained payload": [
    "Saqlangan kontentni ko‘rish",
    "Просмотреть сохранённое содержимое"
  ],
  "Inspect request {p0}": [
    "{p0} so‘rovini ko‘rish",
    "Просмотреть запрос {p0}"
  ],
  "Invalid": [
    "Noto‘g‘ri",
    "Недействительно"
  ],
  "Invalid — authentication rejected. Rotate the key or check its provider account.": [
    "Noto‘g‘ri — autentifikatsiya rad etildi. Kalitni almashtiring yoki provayder akkauntini tekshiring.",
    "Недействительно — аутентификация отклонена. Замените ключ или проверьте аккаунт провайдера."
  ],
  "Invitation accepted. Your workspace access has been updated.": [
    "Taklif qabul qilindi. Ish maydoni ruxsati yangilandi.",
    "Приглашение принято. Доступ к рабочему пространству обновлён."
  ],
  "Invitation issued. Ask the recipient to check their email; delivery may still be pending.": [
    "Taklif yaratildi. Qabul qiluvchi pochtasini tekshirsin; yetkazish hali kutilayotgan bo‘lishi mumkin.",
    "Приглашение выпущено. Попросите получателя проверить почту; доставка может ещё ожидаться."
  ],
  "Invitation role": [
    "Taklif roli",
    "Роль приглашения"
  ],
  "Invitation token": [
    "Taklif tokeni",
    "Токен приглашения"
  ],
  "Invitations": [
    "Takliflar",
    "Приглашения"
  ],
  "Invite a member": [
    "A’zo taklif qilish",
    "Пригласить участника"
  ],
  "Invite member": [
    "A’zo taklif qilish",
    "Пригласить участника"
  ],
  "Invite people, assign the right role and grant only the projects they need.": [
    "Odamlarni taklif qiling, mos rol va faqat kerakli loyiha ruxsatini bering.",
    "Приглашайте людей, назначайте нужную роль и доступ только к необходимым проектам."
  ],
  "Issue credentials, narrow permissions and control spend without losing history.": [
    "Kalitlar yarating, ruxsatlarni toraytiring va tarixni yo‘qotmay sarfni nazorat qiling.",
    "Выпускайте ключи, ограничивайте разрешения и контролируйте расходы без потери истории."
  ],
  "KEY CONTROLS": [
    "KALIT BOSHQARUVI",
    "УПРАВЛЕНИЕ КЛЮЧАМИ"
  ],
  "Key": [
    "Kalit",
    "Ключ"
  ],
  "Key / provider": [
    "Kalit / provayder",
    "Ключ / провайдер"
  ],
  "Key controls": [
    "Kalit boshqaruvi",
    "Управление ключом"
  ],
  "Key copied. Store it securely.": [
    "Kalit nusxalandi. Xavfsiz saqlang.",
    "Ключ скопирован. Храните безопасно."
  ],
  "Key created. Copy the show-once secret.": [
    "Kalit yaratildi. Bir marta ko‘rsatilgan maxfiy kalitni nusxalang.",
    "Ключ создан. Скопируйте одноразово отображаемый секрет."
  ],
  "Key name": [
    "Kalit nomi",
    "Название ключа"
  ],
  "Key permissions": [
    "Kalit ruxsatlari",
    "Разрешения ключа"
  ],
  "Key permissions saved. Deny rules always win.": [
    "Kalit ruxsatlari saqlandi. Taqiqlar har doim ustun.",
    "Разрешения ключа сохранены. Запреты всегда важнее."
  ],
  "Key updated.": [
    "Kalit yangilandi.",
    "Ключ обновлён."
  ],
  "Key usage": [
    "Kalitdan foydalanish",
    "Использование ключа"
  ],
  "Key usage · last 30 days": [
    "Kalitdan foydalanish · oxirgi 30 kun",
    "Использование ключа · последние 30 дней"
  ],
  "Keys": [
    "Kalitlar",
    "Ключи"
  ],
  "Last active Owner · cannot demote or revoke": [
    "Oxirgi faol Ega · rolini pasaytirish yoki chiqarish mumkin emas",
    "Последний активный Владелец · нельзя понизить роль или отозвать доступ"
  ],
  "Last checked": [
    "Oxirgi tekshiruv",
    "Последняя проверка"
  ],
  "Last recorded: {p0}": [
    "Oxirgi yozuv: {p0}",
    "Последняя запись: {p0}"
  ],
  "Last triggered:": [
    "Oxirgi ishga tushish:",
    "Последнее срабатывание:"
  ],
  "Late provider spend": [
    "Kech kelgan provayder sarfi",
    "Поздние расходы провайдера"
  ],
  "Latency": [
    "Kechikish",
    "Задержка"
  ],
  "Latest 100 events, newest first. Pending means no recorded acknowledgement; retries or exhausted work may still need support. Delivered means transport accepted, not that a person read it. Suppressed means delivery was stopped. At-least-once delivery can duplicate a message.": [
    "Oxirgi 100 hodisa, yangisi avval. Kutilmoqda tasdiq qayd etilmaganini bildiradi; urinish yoki tugagan ish uchun yordam kerak bo‘lishi mumkin. Yetkazilgan — transport qabul qilgan, odam o‘qigan degani emas. To‘xtatilgan — yetkazish to‘xtagan. Kamida-bir-marta yetkazish xabarni takrorlashi mumkin.",
    "Последние 100 событий, новые сначала. Ожидание означает отсутствие подтверждения; повторы или исчерпанные попытки могут требовать поддержки. Доставлено — транспорт принял, не человек прочитал. Подавлено — доставка остановлена. Доставка «как минимум один раз» может дублировать сообщения."
  ],
  "Lifetime": [
    "Butun muddat",
    "За всё время"
  ],
  "Lifetime external cap": [
    "Umumiy tashqi limit",
    "Общий внешний лимит"
  ],
  "Lifetime external cap (USD)": [
    "Umumiy tashqi limit (USD)",
    "Общий внешний лимит (USD)"
  ],
  "Limits & budgets": [
    "Limitlar va byudjetlar",
    "Лимиты и бюджеты"
  ],
  "Limits must be whole positive numbers at or below the operator ceiling. Blank inherits.": [
    "Limitlar operator chegarasidan oshmagan butun musbat son bo‘lsin. Bo‘sh maydon meros oladi.",
    "Лимиты — положительные целые числа не выше ограничения оператора. Пустое поле наследует."
  ],
  "Link expired. Close and generate a new link.": [
    "Havola eskirdi. Yoping va yangi havola yarating.",
    "Ссылка истекла. Закройте и создайте новую."
  ],
  "Load more": [
    "Yana yuklash",
    "Загрузить ещё"
  ],
  "Loading API keys…": [
    "API kalitlari yuklanmoqda…",
    "Загрузка API-ключей…"
  ],
  "Loading activity…": [
    "Faoliyat yuklanmoqda…",
    "Загрузка активности…"
  ],
  "Loading alert configuration…": [
    "Ogohlantirish sozlamalari yuklanmoqda…",
    "Загрузка оповещений…"
  ],
  "Loading analytics…": [
    "Tahlil yuklanmoqda…",
    "Загрузка аналитики…"
  ],
  "Loading delivery evidence…": [
    "Yetkazish dalillari yuklanmoqda…",
    "Загрузка сведений о доставке…"
  ],
  "Loading invitations…": [
    "Takliflar yuklanmoqda…",
    "Загрузка приглашений…"
  ],
  "Loading key usage…": [
    "Kalitdan foydalanish yuklanmoqda…",
    "Загрузка использования ключа…"
  ],
  "Loading preview configuration…": [
    "Ko‘rib chiqish sozlamalari yuklanmoqda…",
    "Загрузка конфигурации предпросмотра…"
  ],
  "Loading project retention policy…": [
    "Loyiha saqlash siyosati yuklanmoqda…",
    "Загрузка политики хранения проекта…"
  ],
  "Loading project settings…": [
    "Loyiha sozlamalari yuklanmoqda…",
    "Загрузка настроек проекта…"
  ],
  "Loading projects…": [
    "Loyihalar yuklanmoqda…",
    "Загрузка проектов…"
  ],
  "Loading provider keys…": [
    "Provayder kalitlari yuklanmoqda…",
    "Загрузка ключей провайдеров…"
  ],
  "Loading published models…": [
    "E’lon qilingan modellar yuklanmoqda…",
    "Загрузка опубликованных моделей…"
  ],
  "Loading request limits…": [
    "So‘rov limitlari yuklanmoqda…",
    "Загрузка лимитов запросов…"
  ],
  "Loading request…": [
    "So‘rov yuklanmoqda…",
    "Загрузка запроса…"
  ],
  "Loading spend budgets…": [
    "Sarf byudjetlari yuklanmoqda…",
    "Загрузка бюджетов расходов…"
  ],
  "Loading team access…": [
    "Jamoa ruxsati yuklanmoqda…",
    "Загрузка доступа команды…"
  ],
  "Loading your workspace…": [
    "Ish maydoningiz yuklanmoqda…",
    "Загрузка рабочего пространства…"
  ],
  "Loading…": [
    "Yuklanmoqda…",
    "Загрузка…"
  ],
  "Low balance": [
    "Past balans",
    "Низкий баланс"
  ],
  "Low balance uses available wallet credits. Budget use includes captured spend + reservations. Error spike uses completed requests over five minutes, with at least 20 samples. Persistent breaches are deduplicated until recovery/new budget window. Provider outages remain operator alerts.": [
    "Past balans mavjud kreditdan foydalanadi. Byudjet sarflangan va band mablag‘ni qo‘shadi. Xatolar ko‘payishi kamida 20 namunali besh daqiqada tugagan so‘rovlarga qaraydi. Doimiy buzilish tiklanish/yangi davrgacha takrorlanmaydi. Provayder uzilishi operator ogohlantirishi.",
    "Низкий баланс использует доступный кредит. Бюджет учитывает списания и резервы. Всплеск ошибок — завершённые запросы за пять минут, минимум 20 образцов. Постоянные нарушения не дублируются до восстановления/нового периода. Сбои провайдеров остаются оповещениями оператора."
  ],
  "LowBalance": [
    "Past balans",
    "Низкий баланс"
  ],
  "Lowest latency": [
    "Eng past kechikish",
    "Минимальная задержка"
  ],
  "Lowest price": [
    "Eng arzon",
    "Минимальная цена"
  ],
  "MANAGED CREDITS": [
    "BOSHQARILADIGAN KREDITLAR",
    "КРЕДИТЫ MANAGED"
  ],
  "MODEL DETAIL": [
    "MODEL TAFSILOTI",
    "СВЕДЕНИЯ О МОДЕЛИ"
  ],
  "Main navigation": [
    "Asosiy navigatsiya",
    "Основная навигация"
  ],
  "Manage API keys": [
    "API kalitlarini boshqarish",
    "Управлять API-ключами"
  ],
  "Manage provider keys": [
    "Provayder kalitlarini boshqarish",
    "Управлять ключами провайдеров"
  ],
  "Managed": [
    "Boshqariladigan",
    "Managed"
  ],
  "Managed (default)": [
    "Boshqariladigan (standart)",
    "Managed (по умолчанию)"
  ],
  "Managed credits": [
    "Boshqariladigan kreditlar",
    "Кредиты Managed"
  ],
  "Managed credits · USD": [
    "Boshqariladigan kreditlar · USD",
    "Кредиты Managed · USD"
  ],
  "Managed fallback": [
    "Boshqariladigan fallback",
    "Fallback Managed"
  ],
  "Managed is always the default. Strategy is configurable; model/provider permissions, budgets, wallet and operator limits are mandatory. No provider key or fallback consent is stored in project defaults.": [
    "Boshqariladigan rejim har doim standart. Strategiya sozlanadi; model/provayder ruxsatlari, byudjet, hamyon va operator limitlari majburiy. Loyiha standartida provayder kaliti yoki fallback roziligi saqlanmaydi.",
    "Managed всегда по умолчанию. Стратегия настраивается; разрешения моделей/провайдеров, бюджеты, кошелёк и лимиты оператора обязательны. Ключ провайдера и согласие fallback в настройках проекта не сохраняются."
  ],
  "Managed model alternative": [
    "Boshqariladigan muqobil model",
    "Альтернативная модель Managed"
  ],
  "Managed primary → eligible same-model failover": [
    "Asosiy boshqariladigan → mos bir xil modelga o‘tish",
    "Основной Managed → допустимое переключение на ту же модель"
  ],
  "Managed spending is on hold.": [
    "Boshqariladigan sarf to‘xtatilgan.",
    "Расходы Managed приостановлены."
  ],
  "Management access confirmed.": [
    "Boshqaruv ruxsati tasdiqlangan.",
    "Доступ управления подтверждён."
  ],
  "Max input price · USD / 1M": [
    "Maks. kirish narxi · USD / 1 mln",
    "Макс. цена ввода · USD / 1 млн"
  ],
  "Max output": [
    "Maks. chiqish",
    "Макс. вывод"
  ],
  "Member email": [
    "A’zo pochtasi",
    "Почта участника"
  ],
  "Members": [
    "A’zolar",
    "Участники"
  ],
  "Metadata only. Does not change billing, permissions or execution mode.": [
    "Faqat metadata. Hisob-kitob, ruxsat yoki bajarish rejimini o‘zgartirmaydi.",
    "Только метаданные. Не меняет биллинг, разрешения или режим выполнения."
  ],
  "Min context tokens": [
    "Min. kontekst tokenlari",
    "Мин. токенов контекста"
  ],
  "Model": [
    "Model",
    "Модель"
  ],
  "Model & provider permissions": [
    "Model va provayder ruxsatlari",
    "Разрешения моделей и провайдеров"
  ],
  "Model & spend restrictions": [
    "Model va sarf cheklovlari",
    "Ограничения моделей и расходов"
  ],
  "Model access": [
    "Model ruxsati",
    "Доступ к моделям"
  ],
  "Model catalog or managed pricing is unavailable. Try again later.": [
    "Model katalogi yoki boshqariladigan narxlar mavjud emas. Keyinroq urinib ko‘ring.",
    "Каталог моделей или цены Managed недоступны. Повторите позже."
  ],
  "Model detail": [
    "Model tafsiloti",
    "Сведения о модели"
  ],
  "Model discovery is scoped to an active project.": [
    "Modellarni ko‘rish faol loyihaga bog‘liq.",
    "Поиск моделей ограничен активным проектом."
  ],
  "Model name or ID": [
    "Model nomi yoki ID",
    "Название или ID модели"
  ],
  "Model results": [
    "Model natijalari",
    "Результаты моделей"
  ],
  "Models": [
    "Modellar",
    "Модели"
  ],
  "Monthly": [
    "Oylik",
    "Ежемесячно"
  ],
  "More activity could not be loaded. Retry below.": [
    "Qo‘shimcha faoliyat yuklanmadi. Quyida qayta urinib ko‘ring.",
    "Не удалось загрузить следующую активность. Повторите ниже."
  ],
  "NEW WORKLOAD": [
    "YANGI ISH YUKI",
    "НОВАЯ НАГРУЗКА"
  ],
  "Name or public prefix": [
    "Nom yoki ochiq prefiks",
    "Название или публичный префикс"
  ],
  "Net charge · USD": [
    "Sof to‘lov · USD",
    "Чистое списание · USD"
  ],
  "Never replay after partial output, authentication failure, timeout or unknown execution. An unavailable/ungranted/capped BYOK key fails admission, not a silent Managed switch. Managed pause may remove Hybrid fallback. Project/key policies, budget and wallet holds, provider capability/health/quota and operator limits are rechecked by Gateway. This preview has no API-key permission context and cannot authorize or guarantee a request.": [
    "Qisman chiqish, autentifikatsiya xatosi, timeout yoki noma’lum bajarishdan keyin qayta ijro yo‘q. Mavjud bo‘lmagan/ruxsatsiz/limitli BYOK kalit qabulda rad etiladi, yashirincha Managed ga o‘tmaydi. Managed to‘xtashi Hybrid fallbackni olib tashlashi mumkin. Gateway loyiha/kalit siyosati, byudjet/hamyon bandi, provayder imkoniyati/sog‘ligi/kvotasi va operator limitlarini qayta tekshiradi. Bu ko‘rib chiqishda API kalit ruxsat doirasi yo‘q; u so‘rovni ruxsatlantirmaydi yoki kafolatlamaydi.",
    "Нельзя повторять после частичного вывода, ошибки аутентификации, таймаута или неизвестного выполнения. Недоступный/неразрешённый/исчерпавший лимит ключ BYOK отклоняется при допуске, не переключается скрыто на Managed. Приостановка Managed может убрать fallback Hybrid. Gateway перепроверяет политики проекта/ключа, резервы бюджета/кошелька, возможности/состояние/квоты провайдера и лимиты оператора. Этот предпросмотр не имеет контекста разрешений API-ключа и не авторизует и не гарантирует запрос."
  ],
  "New keys and changes are unavailable. Existing usage and financial history remain available.": [
    "Yangi kalit va o‘zgarishlar mavjud emas. Eski foydalanish va moliyaviy tarix ko‘rinadi.",
    "Новые ключи и изменения недоступны. История использования и финансов остаётся доступной."
  ],
  "New keys inherit project restrictions. Configure permissions and budgets before using the secret. Those are separate API operations.": [
    "Yangi kalit loyiha cheklovlarini meros oladi. Maxfiy kalit ishlatilishidan oldin ruxsat va byudjetlarni sozlang. Bular alohida API amallari.",
    "Новые ключи наследуют ограничения проекта. Настройте разрешения и бюджеты до использования секрета. Это отдельные операции API."
  ],
  "New password": [
    "Yangi parol",
    "Новый пароль"
  ],
  "New projects inherit managed execution and operator limits. Configure budgets separately before issuing production keys.": [
    "Yangi loyiha boshqariladigan rejim va operator limitlarini meros oladi. Prod kalitlaridan oldin byudjetni alohida sozlang.",
    "Новые проекты наследуют Managed и лимиты оператора. Настройте бюджеты отдельно до выпуска production-ключей."
  ],
  "New requests using this key will be denied. History and already admitted requests remain intact. You can enable this key later.": [
    "Bu kalit bilan yangi so‘rovlar rad etiladi. Tarix va qabul qilingan so‘rovlar saqlanadi. Kalitni keyin yoqish mumkin.",
    "Новые запросы с этим ключом отклоняются. История и уже допущенные запросы сохраняются. Ключ можно включить позже."
  ],
  "New role": [
    "Yangi rol",
    "Новая роль"
  ],
  "New top-up": [
    "Yangi to‘ldirish",
    "Новое пополнение"
  ],
  "No API keys for this project.": [
    "Bu loyiha uchun API kalitlari yo‘q.",
    "У проекта нет API-ключей."
  ],
  "No complete response was retained. Interrupted or oversized responses may be absent; this does not imply zero provider cost.": [
    "To‘liq javob saqlanmagan. Uzilgan yoki katta javob yo‘q bo‘lishi mumkin; bu provayder xarajati nol degani emas.",
    "Полный ответ не сохранён. Прерванные или слишком большие ответы могут отсутствовать; это не означает нулевую стоимость провайдера."
  ],
  "No data yet.": [
    "Hali ma’lumot yo‘q.",
    "Данных пока нет."
  ],
  "No delivery events recorded. An enabled rule is not evidence that a message was sent.": [
    "Yetkazish hodisasi qayd etilmagan. Yoqilgan qoida xabar yuborilganiga dalil emas.",
    "Событий доставки нет. Включённое правило не доказывает отправку сообщения."
  ],
  "No destinations yet. Connect one before creating a rule.": [
    "Hali manzillar yo‘q. Qoida yaratishdan oldin birini ulang.",
    "Получателей пока нет. Подключите одного перед созданием правила."
  ],
  "No expiry": [
    "Muddatsiz",
    "Без срока"
  ],
  "No grant means no use. Each change is independent; granting never enables BYOK by default.": [
    "Ruxsatsiz foydalanib bo‘lmaydi. Har o‘zgarish alohida; ruxsat BYOKni standart yoqmaydi.",
    "Без разрешения использование невозможно. Каждое изменение независимо; предоставление доступа не включает BYOK по умолчанию."
  ],
  "No invitations yet.": [
    "Hali takliflar yo‘q.",
    "Приглашений пока нет."
  ],
  "No matching keys.": [
    "Mos kalitlar yo‘q.",
    "Подходящих ключей нет."
  ],
  "No matching members.": [
    "Mos a’zolar yo‘q.",
    "Подходящих участников нет."
  ],
  "No model selected": [
    "Model tanlanmagan",
    "Модель не выбрана"
  ],
  "No options? Ask an operator to activate a supported catalog provider.": [
    "Variant yo‘qmi? Operatordan katalogdagi qo‘llanadigan provayderni faollashtirishni so‘rang.",
    "Нет вариантов? Попросите оператора активировать поддерживаемого провайдера каталога."
  ],
  "No payments yet. Balances update only after provider confirmation.": [
    "Hali to‘lovlar yo‘q. Balans faqat provayder tasdig‘idan keyin yangilanadi.",
    "Платежей пока нет. Баланс обновляется только после подтверждения провайдера."
  ],
  "No pin — eligible providers": [
    "Provayder mahkamlanmagan — mos provayderlar",
    "Без фиксации — допустимые провайдеры"
  ],
  "No project": [
    "Loyiha yo‘q",
    "Нет проекта"
  ],
  "No project access. This member cannot access project-scoped resources yet.": [
    "Loyiha ruxsati yo‘q. A’zo hali loyiha resurslariga kira olmaydi.",
    "Нет доступа к проектам. Участник пока не может использовать ресурсы проектов."
  ],
  "No project selected": [
    "Loyiha tanlanmagan",
    "Проект не выбран"
  ],
  "No projects": [
    "Loyihalar yo‘q",
    "Нет проектов"
  ],
  "No projects granted.": [
    "Ruxsat berilgan loyihalar yo‘q.",
    "Нет разрешённых проектов."
  ],
  "No projects yet. Create one to isolate keys, usage and budgets.": [
    "Hali loyiha yo‘q. Kalit, foydalanish va byudjetlarni ajratish uchun loyiha yarating.",
    "Проектов пока нет. Создайте проект, чтобы разделить ключи, использование и бюджеты."
  ],
  "No provider call was made.": [
    "Provayder chaqirilmagan.",
    "Вызова провайдера не было."
  ],
  "No provider keys. Add an active supported provider, then grant projects separately. Failed grant saves do not undo a created credential.": [
    "Provayder kalitlari yo‘q. Faol provayder qo‘shib, loyiha ruxsatlarini alohida bering. Ruxsatni saqlash xatosi yaratilgan kalitni bekor qilmaydi.",
    "Ключей провайдеров нет. Добавьте активного провайдера и отдельно предоставьте проекты. Ошибка сохранения доступа не отменяет созданный ключ."
  ],
  "No provider usage evidence recorded yet.": [
    "Provayderdan foydalanish dalili hali yo‘q.",
    "Сведений об использовании провайдера пока нет."
  ],
  "No published models match these filters.": [
    "Filtrga mos e’lon qilingan modellar yo‘q.",
    "Нет опубликованных моделей по этим фильтрам."
  ],
  "No recorded test time.": [
    "Tekshiruv vaqti qayd etilmagan.",
    "Время проверки не записано."
  ],
  "No request data for this period.": [
    "Bu davr uchun so‘rovlar yo‘q.",
    "Нет запросов за этот период."
  ],
  "No requests match this window and filters.": [
    "Davr va filtrga mos so‘rovlar yo‘q.",
    "Нет запросов по выбранному периоду и фильтрам."
  ],
  "No reset · all time": [
    "Yangilanmaydi · butun muddat",
    "Без сброса · всё время"
  ],
  "No rules. Start with a low-balance alert.": [
    "Qoidalar yo‘q. Past balans ogohlantirishidan boshlang.",
    "Правил нет. Начните с оповещения о низком балансе."
  ],
  "No unresolved usage flag": [
    "Ochiq foydalanish belgisi yo‘q",
    "Нет флага неурегулированного использования"
  ],
  "No wallet credit posted": [
    "Hamyonga kredit yozilmagan",
    "Кредит в кошелёк не зачислен"
  ],
  "No wallet credit was posted for this payment.": [
    "Bu to‘lov uchun hamyonga kredit yozilmagan.",
    "По этому платежу кредит в кошелёк не зачислен."
  ],
  "No wallet-credit refunds recorded.": [
    "Hamyon qaytarishlari qayd etilmagan.",
    "Возвраты в кошелёк не записаны."
  ],
  "Non-streaming": [
    "Oqimsiz",
    "Без потоковой передачи"
  ],
  "None": [
    "Yo‘q",
    "Нет"
  ],
  "Not configured": [
    "Sozlanmagan",
    "Не настроено"
  ],
  "Not created": [
    "Yaratilmagan",
    "Не создано"
  ],
  "Not recorded": [
    "Qayd etilmagan",
    "Не записано"
  ],
  "Not selected": [
    "Tanlanmagan",
    "Не выбрано"
  ],
  "Not tested — Active means enabled locally, not verified or healthy.": [
    "Tekshirilmagan — Faol faqat mahalliy yoqilganini bildiradi, tasdiqlangan yoki sog‘lom emas.",
    "Не проверено — «Активно» означает локальное включение, не подтверждение или доступность."
  ],
  "ORGANIZATION ACCESS ·": [
    "TASHKILOT RUXSATI ·",
    "ДОСТУП ОРГАНИЗАЦИИ ·"
  ],
  "ORGANIZATION CREDENTIALS ·": [
    "TASHKILOT KALITLARI ·",
    "КЛЮЧИ ОРГАНИЗАЦИИ ·"
  ],
  "OWNER-ONLY CONTROLS ·": [
    "FAQAT EGA BOSHQARUVI ·",
    "УПРАВЛЕНИЕ ТОЛЬКО ВЛАДЕЛЬЦЕМ ·"
  ],
  "Oldest hourly snapshot: {p0}.": [
    "Eng eski soatlik nusxa: {p0}.",
    "Самый старый часовой снимок: {p0}."
  ],
  "One API.": [
    "Bitta API.",
    "Один API."
  ],
  "One canonical code per line": [
    "Har qatorda bitta kanonik kod",
    "Один канонический код в строке"
  ],
  "One canonical code per line; blank denies none": [
    "Har qatorda bitta kanonik kod; bo‘sh hech narsani taqiqlamaydi",
    "Один канонический код в строке; пустое поле ничего не запрещает"
  ],
  "One use · 7 days": [
    "Bir martalik · 7 kun",
    "Одноразово · 7 дней"
  ],
  "Only a current organization Owner can read/change retention or retrieve a body. Admin is not sufficient.": [
    "Faqat joriy tashkilot Egasi saqlashni o‘qishi/o‘zgartirishi yoki kontentni olishi mumkin. Administrator yetarli emas.",
    "Только текущий Владелец организации может читать/менять хранение и получать содержимое. Роли Админ недостаточно."
  ],
  "Only after an eligible verified pre-execution rejection, with explicit consent and wallet hold.": [
    "Faqat bajarishdan oldingi mos tasdiqlangan rad etishdan so‘ng, aniq rozilik va hamyon bandi bilan.",
    "Только после допустимого подтверждённого отказа до выполнения, с явным согласием и резервом кошелька."
  ],
  "Only an Owner may change this member": [
    "Bu a’zoni faqat Ega o‘zgartiradi",
    "Только Владелец может изменить этого участника"
  ],
  "Only current Owners and Admins may read or manage BYOK credentials.": [
    "Faqat joriy Ega va Administrator BYOK kalitlarini ko‘radi va boshqaradi.",
    "Только текущие Владельцы и Админы могут читать и управлять ключами BYOK."
  ],
  "Only explicit grants count for Developer and Read-only. Each grant/removal is a separate audited API operation, not a bulk replacement.": [
    "Dasturchi va Faqat o‘qish uchun faqat aniq ruxsat hisoblanadi. Har berish/olish alohida auditli API amali, ommaviy almashtirish emas.",
    "Для Разработчика и Только чтение учитываются лишь явные разрешения. Каждое предоставление/отзыв — отдельная операция API с аудитом, не массовая замена."
  ],
  "Only selected codes": [
    "Faqat tanlangan kodlar",
    "Только выбранные коды"
  ],
  "Open": [
    "Ochish",
    "Открыть"
  ],
  "Open payload inspector": [
    "Kontent tekshiruvini ochish",
    "Открыть просмотр содержимого"
  ],
  "Open private chat in Telegram": [
    "Telegramda shaxsiy chat ochish",
    "Открыть личный чат в Telegram"
  ],
  "Open project activity and filter by key": [
    "Loyiha faoliyatini ochib, kalit bo‘yicha filtrlang",
    "Открыть активность проекта и фильтр по ключу"
  ],
  "Open secure": [
    "Xavfsiz oynani ochish:",
    "Открыть безопасную оплату"
  ],
  "Open workspace": [
    "Ish maydonini ochish",
    "Открыть рабочее пространство"
  ],
  "Opt-out deletes stored payload rows for this project transactionally. It does not erase activity, usage evidence, financial ledger, audit history or provider-side data.": [
    "O‘chirish bu loyihaning kontent qatorlarini tranzaksiyada o‘chiradi. Faoliyat, foydalanish dalili, moliyaviy daftar, audit tarixi yoki provayder ma’lumotlari o‘chmaydi.",
    "Отключение удаляет строки содержимого проекта транзакционно. Активность, сведения об использовании, финансовый журнал, аудит и данные провайдера не удаляются."
  ],
  "Optional request-only model fallback": [
    "Ixtiyoriy, faqat so‘rovga model fallback",
    "Необязательный fallback моделей только для запроса"
  ],
  "Ordinary Activity stays payload-free. Paste a request ID from this project to perform one owner-authorized, audited read. No automatic body fetch, query cache, browser storage or export.": [
    "Oddiy Faoliyatda kontent yo‘q. Ega ruxsati bilan auditli bir o‘qish uchun shu loyiha so‘rov ID sini kiriting. Avtomatik olish, kesh, brauzer xotirasi yoki eksport yo‘q.",
    "Обычная Активность не содержит тел запросов. Вставьте ID запроса проекта для одного аудируемого чтения Владельцем. Нет автоматической загрузки, кеша, хранения в браузере или экспорта."
  ],
  "Organization": [
    "Tashkilot",
    "Организация"
  ],
  "Organization name": [
    "Tashkilot nomi",
    "Название организации"
  ],
  "Organization-wide alerts · financial reads require Owner, Admin or BillingViewer. Only Owner/Admin can change rules or destinations. Project selection does not filter this page.": [
    "Tashkilot bo‘ylab ogohlantirishlar · moliyaviy o‘qish uchun Ega, Administrator yoki Hisob-kitob kuzatuvchisi kerak. Faqat Ega/Administrator qoida va manzillarni o‘zgartiradi. Loyiha tanlovi bu sahifani filtrlamaydi.",
    "Оповещения всей организации · финансовое чтение требует Владельца, Админа или Наблюдателя биллинга. Правила/получателей меняют только Владелец/Админ. Выбор проекта не фильтрует эту страницу."
  ],
  "Organization-wide role permissions": [
    "Tashkilot bo‘ylab rol ruxsatlari",
    "Разрешения ролей организации"
  ],
  "Other budgets and wallet checks still apply.": [
    "Boshqa byudjet va hamyon tekshiruvlari ham amal qiladi.",
    "Другие бюджеты и проверки кошелька также применяются."
  ],
  "Outcome or provider usage is unknown. Reconciliation may change the final charge; this does not mean the provider billed zero.": [
    "Natija yoki provayder foydalanishi noma’lum. Hisoblarni solishtirish yakuniy to‘lovni o‘zgartirishi mumkin; provayder nol hisoblagan degani emas.",
    "Результат или использование провайдера неизвестны. Сверка может изменить итоговое списание; это не означает нулевой счёт провайдера."
  ],
  "OutcomeUnknown": [
    "Natija noma’lum",
    "Результат неизвестен"
  ],
  "Output": [
    "Chiqish",
    "Вывод"
  ],
  "Outside exact browser precision": [
    "Brauzerning aniq qiymat chegarasidan tashqarida",
    "За пределами точности браузера"
  ],
  "Outstanding recovery amount; not spendable credit": [
    "To‘lanmagan tiklash qarzi; sarflanadigan kredit emas",
    "Непогашенная задолженность восстановления; не доступный кредит"
  ],
  "Overview": [
    "Umumiy ko‘rinish",
    "Обзор"
  ],
  "Owner": [
    "Ega",
    "Владелец"
  ],
  "Owner access required": [
    "Ega ruxsati kerak",
    "Требуется доступ Владельца"
  ],
  "Owner or Admin access required": [
    "Ega yoki Administrator ruxsati kerak",
    "Требуется доступ Владельца или Админа"
  ],
  "Owner-only, audited access. Closing, switching scope or local expiry clears this view. Do not share private prompt/response content.": [
    "Faqat Ega, audit bilan. Yopish, doirani almashtirish yoki mahalliy muddat tugashi ko‘rinishni tozalaydi. Maxfiy prompt/javobni ulashmang.",
    "Только Владелец, с аудитом. Закрытие, смена области или локальное истечение очищает просмотр. Не делитесь приватным запросом/ответом."
  ],
  "PRIVATE · THIS DIALOG ONLY": [
    "MAXFIY · FAQAT SHU OYNA",
    "ПРИВАТНО · ТОЛЬКО ЭТО ОКНО"
  ],
  "PROJECT ACCESS ·": [
    "LOYIHA RUXSATI ·",
    "ДОСТУП ПРОЕКТА ·"
  ],
  "PUBLISHED MANAGED CATALOG": [
    "E’LON QILINGAN BOSHQARILADIGAN KATALOG",
    "ОПУБЛИКОВАННЫЙ КАТАЛОГ MANAGED"
  ],
  "Paid": [
    "To‘langan",
    "Оплачено"
  ],
  "Password": [
    "Parol",
    "Пароль"
  ],
  "Password must be between 12 and 128 characters.": [
    "Parol 12–128 belgi bo‘lishi kerak.",
    "Пароль должен содержать 12–128 символов."
  ],
  "Password updated. Sign in with your new password.": [
    "Parol yangilandi. Yangi parol bilan kiring.",
    "Пароль обновлён. Войдите с новым паролем."
  ],
  "Paste the complete 43-character invitation token from your email.": [
    "Pochtadagi to‘liq 43 belgili taklif tokenini kiriting.",
    "Вставьте полный токен приглашения из почты длиной 43 символа."
  ],
  "Pause": [
    "To‘xtatish",
    "Приостановить"
  ],
  "Pause rule?": [
    "Qoida to‘xtatilsinmi?",
    "Приостановить правило?"
  ],
  "Payload expired. Its body has been cleared from this view.": [
    "Kontent muddati tugadi. Ushbu ko‘rinishdan tozalandi.",
    "Срок содержимого истёк. Оно очищено из просмотра."
  ],
  "Payload retention is off by default. This project-only setting does not change provider data policies, billing, permissions, or imply zero data retention upstream.": [
    "Kontent saqlash dastlab o‘chiq. Bu loyiha sozlamasi provayder ma’lumot siyosati, hisob-kitob yoki ruxsatni o‘zgartirmaydi va provayderda nol saqlashni anglatmaydi.",
    "Хранение содержимого по умолчанию отключено. Настройка проекта не меняет политику данных провайдера, биллинг, разрешения и не означает нулевое хранение у провайдера."
  ],
  "Payload storage": [
    "Kontent saqlash",
    "Хранение содержимого"
  ],
  "Payload unavailable: never captured, expired, opted out, or outside this project. No body was returned.": [
    "Kontent mavjud emas: saqlanmagan, eskirgan, o‘chirilgan yoki boshqa loyihaga tegishli. Hech qanday kontent qaytmadi.",
    "Содержимое недоступно: не записано, истекло, отключено или вне проекта. Тело не возвращено."
  ],
  "Payment amount differs from the quote. Do not pay; contact support.": [
    "To‘lov summasi taklifga mos emas. To‘lamang; yordam xizmatiga murojaat qiling.",
    "Сумма платежа отличается от расчёта. Не платите; обратитесь в поддержку."
  ],
  "Payment evidence needs investigation. Contact support with the payment reference; do not retry blindly.": [
    "To‘lov dalilini tekshirish kerak. To‘lov raqami bilan yordam xizmatiga murojaat qiling; ko‘r-ko‘rona takrorlamang.",
    "Сведения о платеже требуют проверки. Обратитесь в поддержку с номером платежа; не повторяйте вслепую."
  ],
  "Payment history": [
    "To‘lovlar tarixi",
    "История платежей"
  ],
  "Payment provider": [
    "To‘lov provayderi",
    "Платёжный провайдер"
  ],
  "Payment was accepted locally but wallet credit is not visible yet. Contact support if this persists.": [
    "To‘lov mahalliy qabul qilingan, ammo kredit ko‘rinmayapti. Davom etsa yordam xizmatiga murojaat qiling.",
    "Платёж принят локально, но кредит кошелька пока не виден. Если это сохраняется, обратитесь в поддержку."
  ],
  "Pending": [
    "Kutilmoqda",
    "Ожидание"
  ],
  "Pending charges excluded": [
    "Kutilayotgan to‘lovlar kiritilmagan",
    "Ожидающие списания исключены"
  ],
  "Percentage must be 0.01–100%.": [
    "Foiz 0.01–100% bo‘lishi kerak.",
    "Процент должен быть 0.01–100%."
  ],
  "Permanently crypto-shred this stored secret. Audit and financial history remain; this cannot be undone.": [
    "Saqlangan maxfiy kalitni qaytarib bo‘lmas kripto-o‘chirish. Audit va moliyaviy tarix qoladi; bekor qilib bo‘lmaydi.",
    "Безвозвратно криптографически удалить сохранённый секрет. Аудит и финансовая история остаются; отмена невозможна."
  ],
  "Permission denied. Your current role, project grant or session does not allow this operation. Ask an organization owner; no change was saved.": [
    "Ruxsat rad etildi. Joriy rol, loyiha ruxsati yoki sessiya amalni bajara olmaydi. Tashkilot egasiga murojaat qiling; o‘zgarish saqlanmadi.",
    "Доступ запрещён. Текущая роль, разрешение проекта или сессия не допускает операцию. Обратитесь к владельцу организации; изменение не сохранено."
  ],
  "Platform exposure": [
    "Platforma tavakkali",
    "Риск платформы"
  ],
  "Please wait…": [
    "Kutib turing…",
    "Подождите…"
  ],
  "Prepared": [
    "Tayyorlangan",
    "Подготовлено"
  ],
  "Preparing CSV…": [
    "CSV tayyorlanmoqda…",
    "Подготовка CSV…"
  ],
  "Preparing complete CSV…": [
    "To‘liq CSV tayyorlanmoqda…",
    "Подготовка полного CSV…"
  ],
  "Preview blocked": [
    "Ko‘rib chiqish bloklangan",
    "Предпросмотр заблокирован"
  ],
  "Previously credited funds were reversed. Your wallet and any recovery debt reflect the adjustment.": [
    "Oldin tushgan mablag‘ qaytarib olindi. Hamyon va tiklash qarzi tuzatishni aks ettiradi.",
    "Ранее зачисленные средства отменены. Кошелёк и задолженность восстановления отражают корректировку."
  ],
  "Prices as of": [
    "Narxlar vaqti",
    "Цены по состоянию на"
  ],
  "Primary model": [
    "Asosiy model",
    "Основная модель"
  ],
  "Priority": [
    "Ustuvorlik",
    "Приоритет"
  ],
  "Privacy": [
    "Maxfiylik",
    "Конфиденциальность"
  ],
  "Private chat ID hidden": [
    "Shaxsiy chat ID yashirilgan",
    "ID личного чата скрыт"
  ],
  "Private payload contents": [
    "Maxfiy kontent tarkibi",
    "Приватное содержимое"
  ],
  "Project": [
    "Loyiha",
    "Проект"
  ],
  "Project / model": [
    "Loyiha / model",
    "Проект / модель"
  ],
  "Project access for": [
    "Loyiha ruxsati:",
    "Доступ к проектам для"
  ],
  "Project access updated.": [
    "Loyiha ruxsati yangilandi.",
    "Доступ к проекту обновлён."
  ],
  "Project access ·": [
    "Loyiha ruxsati ·",
    "Доступ к проекту ·"
  ],
  "Project and key limits both apply. Blank inherits the operator ceiling, never unlimited. Existing streams are not cancelled.": [
    "Loyiha va kalit limitlari birga amal qiladi. Bo‘sh operator chegarasini meros oladi, cheksiz emas. Faol oqim bekor qilinmaydi.",
    "Лимиты проекта и ключа действуют вместе. Пустое поле наследует предел оператора, не безлимит. Активные потоки не отменяются."
  ],
  "Project archived.": [
    "Loyiha arxivlandi.",
    "Проект архивирован."
  ],
  "Project budget policy": [
    "Loyiha byudjet siyosati",
    "Бюджетная политика проекта"
  ],
  "Project controls": [
    "Loyiha boshqaruvi",
    "Управление проектом"
  ],
  "Project created. Open it to configure restrictions and budgets.": [
    "Loyiha yaratildi. Cheklov va byudjetlarni sozlash uchun oching.",
    "Проект создан. Откройте для настройки ограничений и бюджетов."
  ],
  "Project limits & budgets": [
    "Loyiha limitlari va byudjetlari",
    "Лимиты и бюджеты проекта"
  ],
  "Project name": [
    "Loyiha nomi",
    "Название проекта"
  ],
  "Project settings": [
    "Loyiha sozlamalari",
    "Настройки проекта"
  ],
  "Project settings saved. Future admissions use these restrictions.": [
    "Loyiha sozlamalari saqlandi. Keyingi qabul shu cheklovdan foydalanadi.",
    "Настройки проекта сохранены. Новые допуски используют эти ограничения."
  ],
  "Project to grant": [
    "Ruxsat beriladigan loyiha",
    "Проект для доступа"
  ],
  "Projects": [
    "Loyihalar",
    "Проекты"
  ],
  "Prompt and response bodies are not retained in this view.": [
    "Bu ko‘rinishda prompt va javob kontenti saqlanmaydi.",
    "Тела запросов и ответов в этом представлении не хранятся."
  ],
  "Provider": [
    "Provayder",
    "Провайдер"
  ],
  "Provider attempts": [
    "Provayder urinishlari",
    "Попытки провайдера"
  ],
  "Provider cost": [
    "Provayder xarajati",
    "Стоимость провайдера"
  ],
  "Provider key access unavailable": [
    "Provayder kalitiga kirish yo‘q",
    "Нет доступа к ключам провайдеров"
  ],
  "Provider keys": [
    "Provayder kalitlari",
    "Ключи провайдеров"
  ],
  "Provider pin": [
    "Provayderni mahkamlash",
    "Фиксация провайдера"
  ],
  "Provider secret": [
    "Provayder maxfiy kaliti",
    "Секрет провайдера"
  ],
  "Provider/model pinning can remove same-model failover; Gateway applies the bounded attempt policy.": [
    "Provayder/modelni mahkamlash bir xil model fallbackini olib tashlashi mumkin; Gateway chegaralangan urinish siyosatini qo‘llaydi.",
    "Фиксация провайдера/модели может убрать переключение на ту же модель; Gateway применяет ограниченную политику попыток."
  ],
  "Public HTTPS hostname on port 443 only. No IP literals, user info, query or fragment. The signing secret is shown once. Creating does not prove endpoint ownership or delivery.": [
    "Faqat 443-portdagi ochiq HTTPS domen. IP, foydalanuvchi ma’lumoti, so‘rov yoki fragment yo‘q. Imzo kaliti bir marta ko‘rsatiladi. Yaratish endpoint egaligi yoki yetkazishni isbotlamaydi.",
    "Только публичный HTTPS-домен на порту 443. Без IP, данных пользователя, query или fragment. Секрет подписи показывается один раз. Создание не доказывает владение endpoint или доставку."
  ],
  "Published provider prices": [
    "E’lon qilingan provayder narxlari",
    "Опубликованные цены провайдеров"
  ],
  "Quickstart · chat completions": [
    "Tezkor boshlash · chat completions",
    "Быстрый старт · chat completions"
  ],
  "Quote amount differs from the requested amount. Please try again.": [
    "Taklif summasi so‘ralgan summadan farq qiladi. Qayta urinib ko‘ring.",
    "Сумма расчёта отличается от запрошенной. Повторите."
  ],
  "REQUEST-ONLY · NEVER AUTO-SAVED": [
    "FAQAT SO‘ROV UCHUN · AVTOMATIK SAQLANMAYDI",
    "ТОЛЬКО ДЛЯ ЗАПРОСА · НЕ СОХРАНЯЕТСЯ АВТОМАТИЧЕСКИ"
  ],
  "ROUTING & EXECUTION ·": [
    "YO‘NALTIRISH VA BAJARISH ·",
    "МАРШРУТИЗАЦИЯ И ВЫПОЛНЕНИЕ ·"
  ],
  "Rates include": [
    "Narxlarga",
    "Цены включают"
  ],
  "Read retained payload": [
    "Saqlangan kontentni o‘qish",
    "Прочитать сохранённое содержимое"
  ],
  "Read-only": [
    "Faqat o‘qish",
    "Только чтение"
  ],
  "Read-only until current Owner/Admin access is confirmed.": [
    "Joriy Ega/Administrator ruxsati tasdiqlanmaguncha faqat o‘qish.",
    "Только чтение до подтверждения текущего доступа Владельца/Админа."
  ],
  "ReadOnly": [
    "Faqat o‘qish",
    "Только чтение"
  ],
  "Rearms this rule; an existing breach can produce another event. A disabled destination still cannot deliver.": [
    "Qoidani qayta yoqadi; mavjud buzilish yangi hodisa berishi mumkin. O‘chirilgan manzil baribir yetkaza olmaydi.",
    "Повторно активирует правило; существующее нарушение может создать новое событие. Отключённый получатель всё равно не доставляет."
  ],
  "Recent deliveries": [
    "Oxirgi yetkazishlar",
    "Последние доставки"
  ],
  "Reconciliation pending": [
    "Hisoblarni solishtirish kutilmoqda",
    "Ожидает сверки"
  ],
  "Recover your account": [
    "Akkauntni tiklash",
    "Восстановить аккаунт"
  ],
  "Recovery debt": [
    "Tiklash qarzi",
    "Задолженность восстановления"
  ],
  "Recovery token": [
    "Tiklash tokeni",
    "Токен восстановления"
  ],
  "Refresh alerts": [
    "Ogohlantirishlarni yangilash",
    "Обновить оповещения"
  ],
  "Refresh keys": [
    "Kalitlarni yangilash",
    "Обновить ключи"
  ],
  "Refresh policy": [
    "Siyosatni yangilash",
    "Обновить политику"
  ],
  "Refresh preview data": [
    "Ko‘rib chiqish ma’lumotini yangilash",
    "Обновить данные предпросмотра"
  ],
  "Refresh team": [
    "Jamoani yangilash",
    "Обновить команду"
  ],
  "Refunded": [
    "Qaytarilgan",
    "Возвращено"
  ],
  "Register": [
    "Ro‘yxatdan o‘tish",
    "Регистрация"
  ],
  "RejectedBeforeExecution": [
    "Bajarishdan oldin rad etilgan",
    "Отклонено до выполнения"
  ],
  "Remove": [
    "Olib tashlash",
    "Удалить"
  ],
  "Remove access to": [
    "Quyidagi ruxsatni olib tashlash",
    "Удалить доступ к"
  ],
  "Remove grant": [
    "Ruxsatni olib tashlash",
    "Удалить разрешение"
  ],
  "Remove project access": [
    "Loyiha ruxsatini olib tashlash",
    "Удалить доступ к проекту"
  ],
  "Remove project grant?": [
    "Loyiha ruxsati olib tashlansinmi?",
    "Удалить разрешение проекта?"
  ],
  "Replacement provider secret (optional)": [
    "Yangi provayder maxfiy kaliti (ixtiyoriy)",
    "Новый секрет провайдера (необязательно)"
  ],
  "Request": [
    "So‘rov",
    "Запрос"
  ],
  "Request ID must be a UUID.": [
    "So‘rov ID UUID bo‘lishi kerak.",
    "ID запроса должен быть UUID."
  ],
  "Request UUID": [
    "So‘rov UUID",
    "UUID запроса"
  ],
  "Request activity": [
    "So‘rov faoliyati",
    "Активность запросов"
  ],
  "Request and bounded complete response bodies are encrypted separately from activity metadata. Shortening the duration also shortens existing records. Expired payload reads are rejected immediately; Worker deletes expired rows in bounded sweeps every five minutes.": [
    "So‘rov va chegaralangan to‘liq javoblar faoliyat metadatasidan alohida shifrlanadi. Muddatni qisqartirish mavjud yozuvni ham qisqartiradi. Eskirgan kontentni o‘qish darhol rad etiladi; Worker har besh daqiqada chegaralangan tozalashda qatorlarni o‘chiradi.",
    "Тела запросов и ограниченных полных ответов шифруются отдельно от метаданных активности. Сокращение срока сокращает и существующие записи. Чтение истёкшего содержимого отклоняется сразу; Worker удаляет строки ограниченными проходами каждые пять минут."
  ],
  "Request details": [
    "So‘rov tafsilotlari",
    "Сведения о запросе"
  ],
  "Request limits": [
    "So‘rov limitlari",
    "Лимиты запросов"
  ],
  "Request limits saved.": [
    "So‘rov limitlari saqlandi.",
    "Лимиты запросов сохранены."
  ],
  "Request preview": [
    "So‘rovni ko‘rib chiqish",
    "Предпросмотр запроса"
  ],
  "Request provider key": [
    "So‘rov provayder kaliti",
    "Ключ провайдера запроса"
  ],
  "Request routing may override strategy only. Project restrictions, key permissions, budgets, wallet and operator limits always apply. Managed is the default; BYOK/Hybrid still require explicit request consent.": [
    "So‘rov yo‘naltirishi faqat strategiyani almashtiradi. Loyiha cheklovi, kalit ruxsati, byudjet, hamyon va operator limitlari doim amal qiladi. Boshqariladigan standart; BYOK/Hybrid aniq so‘rov roziligini talab qiladi.",
    "Маршрутизация запроса переопределяет только стратегию. Ограничения проекта, разрешения ключа, бюджеты, кошелёк и лимиты оператора действуют всегда. По умолчанию Managed; BYOK/Hybrid требуют явного согласия запроса."
  ],
  "Request routing override": [
    "So‘rov yo‘naltirishini almashtirish",
    "Переопределение маршрутизации запроса"
  ],
  "Requested model": [
    "So‘ralgan model",
    "Запрошенная модель"
  ],
  "Requests": [
    "So‘rovlar",
    "Запросы"
  ],
  "Requests by day": [
    "Kunlar bo‘yicha so‘rovlar",
    "Запросы по дням"
  ],
  "Requests per minute": [
    "Daqiqadagi so‘rovlar",
    "Запросы в минуту"
  ],
  "Reserved $": [
    "Band $",
    "Резерв $"
  ],
  "Reserved balance": [
    "Band balans",
    "Зарезервированный баланс"
  ],
  "Resets {p0} (UTC)": [
    "Yangilanish {p0} (UTC)",
    "Сброс {p0} (UTC)"
  ],
  "Restrictions saved. Existing holds remain; future admission rechecks all policies.": [
    "Cheklovlar saqlandi. Mavjud bandlar qoladi; keyingi qabul barcha siyosatni tekshiradi.",
    "Ограничения сохранены. Резервы остаются; новый допуск перепроверяет все политики."
  ],
  "Retain less. Keep financial evidence separate.": [
    "Kamroq saqlang. Moliyaviy dalilni alohida tuting.",
    "Храните меньше. Отделяйте финансовые доказательства."
  ],
  "Retained payload · private": [
    "Saqlangan kontent · maxfiy",
    "Сохранённое содержимое · приватно"
  ],
  "Retained request": [
    "Saqlangan so‘rov",
    "Сохранённый запрос"
  ],
  "Retained response": [
    "Saqlangan javob",
    "Сохранённый ответ"
  ],
  "Retention disabled": [
    "Saqlash o‘chirilgan",
    "Хранение отключено"
  ],
  "Retention duration (minutes)": [
    "Saqlash muddati (daqiqa)",
    "Срок хранения (минуты)"
  ],
  "Retention enabled": [
    "Saqlash yoqilgan",
    "Хранение включено"
  ],
  "Retention must be a whole number from 60 to 10080 minutes.": [
    "Saqlash muddati 60–10080 orasidagi butun daqiqa bo‘lsin.",
    "Срок хранения — целое число минут от 60 до 10080."
  ],
  "Retry": [
    "Qayta urinish",
    "Повторить"
  ],
  "Retry read": [
    "O‘qishni takrorlash",
    "Повторить чтение"
  ],
  "Reversed": [
    "Qaytarib olingan",
    "Отменено зачисление"
  ],
  "Reversed · under review": [
    "Qaytarib olingan · tekshiruvda",
    "Отмена зачисления · на проверке"
  ],
  "Revoke": [
    "Ruxsatni bekor qilish",
    "Отозвать доступ"
  ],
  "Revoke member access": [
    "A’zo ruxsatini bekor qilish",
    "Отозвать доступ участника"
  ],
  "Revoked": [
    "Ruxsat bekor qilingan",
    "Доступ отозван"
  ],
  "Role changes apply on the next authorized request. Switching to Owner/Admin/Billing viewer clears explicit project grants; switching back requires granting projects again.": [
    "Rol o‘zgarishi keyingi ruxsatli so‘rovga tatbiq etiladi. Ega/Administrator/Hisob-kitob kuzatuvchisi ga o‘tish aniq loyiha ruxsatlarini tozalaydi; qaytishda ruxsatni yana berish kerak.",
    "Роль меняется со следующего авторизованного запроса. Переход во Владельца/Админа/Наблюдателя биллинга очищает явные разрешения проектов; при возвращении их нужно выдать снова."
  ],
  "Rotate": [
    "Almashtirish",
    "Ротация"
  ],
  "Routing": [
    "Yo‘naltirish",
    "Маршрутизация"
  ],
  "Routing defaults & hard restrictions": [
    "Yo‘naltirish standartlari va qat’iy cheklovlar",
    "Маршрутизация по умолчанию и обязательные ограничения"
  ],
  "Routing strategy": [
    "Yo‘naltirish strategiyasi",
    "Стратегия маршрутизации"
  ],
  "Rule": [
    "Qoida",
    "Правило"
  ],
  "Rule project": [
    "Qoida loyihasi",
    "Проект правила"
  ],
  "Rule updated.": [
    "Qoida yangilandi.",
    "Правило обновлено."
  ],
  "SELECTED PROJECT": [
    "TANLANGAN LOYIHA",
    "ВЫБРАННЫЙ ПРОЕКТ"
  ],
  "SHOW ONCE · KEEP PRIVATE": [
    "BIR MARTA KO‘RSATILADI · MAXFIY SAQLANG",
    "ПОКАЗЫВАЕТСЯ ОДИН РАЗ · ХРАНИТЕ ПРИВАТНО"
  ],
  "SHOW-ONCE CREDENTIAL": [
    "BIR MARTALIK MAXFIY KALIT",
    "ОДНОРАЗОВЫЙ ПОКАЗ КЛЮЧА"
  ],
  "SIGNAL, NOT NOISE ·": [
    "SIGNAL, SHOVQIN EMAS ·",
    "СИГНАЛ, НЕ ШУМ ·"
  ],
  "Save alert rule": [
    "Ogohlantirish qoidasini saqlash",
    "Сохранить правило оповещения"
  ],
  "Save credential": [
    "Kalitni saqlash",
    "Сохранить ключ"
  ],
  "Save key permissions": [
    "Kalit ruxsatlarini saqlash",
    "Сохранить разрешения ключа"
  ],
  "Save project settings": [
    "Loyiha sozlamalarini saqlash",
    "Сохранить настройки проекта"
  ],
  "Save request limits": [
    "So‘rov limitlarini saqlash",
    "Сохранить лимиты запросов"
  ],
  "Save restrictions": [
    "Cheklovlarni saqlash",
    "Сохранить ограничения"
  ],
  "Save retention policy": [
    "Saqlash siyosatini saqlash",
    "Сохранить политику хранения"
  ],
  "Save spend budget": [
    "Sarf byudjetini saqlash",
    "Сохранить бюджет расходов"
  ],
  "Save this signing secret in your receiver’s secret store now. Closing hides it permanently. Verify HMAC signatures and deduplicate event IDs; see the API contract.": [
    "Imzo maxfiy kalitini hozir qabul qiluvchining maxfiy xotirasida saqlang. Yopish uni butunlay yashiradi. HMAC imzosini tekshiring va hodisa ID larini takrorlamang; API shartnomasiga qarang.",
    "Сохраните секрет подписи в хранилище секретов получателя сейчас. Закрытие скрывает его навсегда. Проверяйте подписи HMAC и устраняйте дубли ID событий; см. контракт API."
  ],
  "Saved.": [
    "Saqlandi.",
    "Сохранено."
  ],
  "Saving Disabled deletes all stored payloads for this project. This cannot be undone; metadata and financial history remain.": [
    "O‘chirilgan ni saqlash bu loyihaning barcha saqlangan kontentini o‘chiradi. Bekor qilib bo‘lmaydi; metadata va moliyaviy tarix qoladi.",
    "Сохранение «Отключено» удаляет всё содержимое проекта. Отмена невозможна; метаданные и финансовая история остаются."
  ],
  "Saving replaces all four lists, last-commit-wins. Price-class restrictions are not supported.": [
    "Saqlash to‘rtta ro‘yxatni almashtiradi; oxirgi yozuv ustun. Narx toifasi cheklovi qo‘llanmaydi.",
    "Сохранение заменяет все четыре списка; последняя запись побеждает. Ограничения ценовых классов не поддерживаются."
  ],
  "Saving…": [
    "Saqlanmoqda…",
    "Сохранение…"
  ],
  "Search keys": [
    "Kalitlarni qidirish",
    "Поиск ключей"
  ],
  "Search members": [
    "A’zolarni qidirish",
    "Поиск участников"
  ],
  "Search models": [
    "Modellarni qidirish",
    "Поиск моделей"
  ],
  "Second fallback model": [
    "Ikkinchi fallback modeli",
    "Вторая модель fallback"
  ],
  "Secrets appear once, at creation or rotation. Only masked prefixes are listed. Disable revokes new admission; it does not delete history or cancel already admitted work.": [
    "Maxfiy kalit yaratilish yoki almashtirishda bir marta ko‘rinadi. Ro‘yxatda niqoblangan prefiks. Faolsizlantirish yangi qabulni bekor qiladi; tarixni o‘chirmaydi yoki qabul qilingan ishni to‘xtatmaydi.",
    "Секреты показываются один раз при создании или ротации. В списке только скрытые префиксы. Отключение отзывает новый допуск, не удаляет историю и не отменяет уже допущенную работу."
  ],
  "Secrets are encrypted and never returned. Adding or granting a key does not change Managed execution: each request must explicitly select BYOK or Hybrid.": [
    "Maxfiy kalitlar shifrlanadi va qaytarilmaydi. Kalit qo‘shish/ruxsat berish Boshqariladigan rejimni o‘zgartirmaydi: har so‘rov BYOK yoki Hybrid ni aniq tanlashi kerak.",
    "Секреты шифруются и не возвращаются. Добавление/предоставление ключа не меняет Managed: каждый запрос должен явно выбрать BYOK или Hybrid."
  ],
  "Select a currently readable provider key; unavailable metadata never changes the request to Managed.": [
    "Hozir o‘qish mumkin bo‘lgan provayder kalitini tanlang; metadata yo‘qligi so‘rovni Boshqariladigan rejimga o‘tkazmaydi.",
    "Выберите доступный для чтения ключ провайдера; отсутствие метаданных не переключает запрос на Managed."
  ],
  "Select a published model. An empty catalog may indicate project restrictions or unavailable pricing.": [
    "E’lon qilingan modelni tanlang. Bo‘sh katalog loyiha cheklovi yoki narx yo‘qligini bildirishi mumkin.",
    "Выберите опубликованную модель. Пустой каталог может указывать на ограничения проекта или недоступные цены."
  ],
  "Select a request to inspect its safe metadata and provider attempts.": [
    "Xavfsiz metadata va provayder urinishlarini ko‘rish uchun so‘rovni tanlang.",
    "Выберите запрос для просмотра безопасных метаданных и попыток провайдера."
  ],
  "Select an active project before configuring routing.": [
    "Yo‘naltirishdan oldin faol loyihani tanlang.",
    "Выберите активный проект перед настройкой маршрутизации."
  ],
  "Selected canonical models": [
    "Tanlangan kanonik modellar",
    "Выбранные канонические модели"
  ],
  "Selected model": [
    "Tanlangan model",
    "Выбранная модель"
  ],
  "Send invitation": [
    "Taklif yuborish",
    "Отправить приглашение"
  ],
  "Separate workloads. Set safe defaults. Keep every key and dollar in the right scope.": [
    "Ishlarni ajrating. Xavfsiz standart qo‘ying. Har kalit va dollarni to‘g‘ri doirada saqlang.",
    "Разделяйте нагрузки. Задавайте безопасные настройки. Держите каждый ключ и доллар в нужной области."
  ],
  "Set a new password": [
    "Yangi parol o‘rnating",
    "Установите новый пароль"
  ],
  "Set a threshold. Choose a destination. Follow the delivery evidence.": [
    "Chegarani belgilang. Manzil tanlang. Yetkazish dalilini kuzating.",
    "Задайте порог. Выберите получателя. Следите за доказательствами доставки."
  ],
  "Settings & restrictions": [
    "Sozlamalar va cheklovlar",
    "Настройки и ограничения"
  ],
  "Settled charges": [
    "Yakunlangan to‘lovlar",
    "Урегулированные списания"
  ],
  "Sign in": [
    "Kirish",
    "Войти"
  ],
  "Sign in to accept": [
    "Qabul qilish uchun kiring",
    "Войдите для принятия"
  ],
  "Sign in to continue.": [
    "Davom etish uchun kiring.",
    "Войдите для продолжения."
  ],
  "Sign out": [
    "Chiqish",
    "Выйти"
  ],
  "Signed in as": [
    "Kirgan akkaunt",
    "Вы вошли как"
  ],
  "Six decimal places. Blank resets to uncapped. Zero/lowering below spent + held blocks future admission, never removes holds.": [
    "Olti kasr xonasi. Bo‘sh cheklanmagan holatga qaytaradi. Nol yoki sarf+banddan past limit keyingi qabulni bloklaydi, bandni olib tashlamaydi.",
    "Шесть знаков после точки. Пустое поле убирает лимит. Ноль/снижение ниже расходов+резервов блокирует новые допуски, не удаляя резервы."
  ],
  "Skip to content": [
    "Kontentga o‘tish",
    "Перейти к содержимому"
  ],
  "Something went wrong. Please try again.": [
    "Xatolik yuz berdi. Qayta urinib ko‘ring.",
    "Произошла ошибка. Повторите."
  ],
  "Spend budget": [
    "Sarf byudjeti",
    "Бюджет расходов"
  ],
  "Spend budgets · USD": [
    "Sarf byudjetlari · USD",
    "Бюджеты расходов · USD"
  ],
  "Start with your organization.": [
    "Tashkilotingizdan boshlang.",
    "Начните с организации."
  ],
  "Started / finished": [
    "Boshlangan / tugagan",
    "Начало / окончание"
  ],
  "Status": [
    "Holat",
    "Статус"
  ],
  "Stop future deliveries to this destination. Rules and financial history stay unchanged; pending messages may be suppressed. This does not revoke a Telegram link already issued.": [
    "Bu manzilga keyingi yetkazishni to‘xtatish. Qoida va moliyaviy tarix o‘zgarmaydi; kutilayotgan xabarlar to‘xtatilishi mumkin. Berilgan Telegram havolasi bekor qilinmaydi.",
    "Остановить будущие доставки получателю. Правила и финансовая история не меняются; ожидающие сообщения могут быть подавлены. Уже выданная ссылка Telegram не отзывается."
  ],
  "Stop future use. There is no re-enable operation; add a new credential if needed.": [
    "Keyingi foydalanishni to‘xtatish. Qayta yoqish yo‘q; kerak bo‘lsa yangi kalit qo‘shing.",
    "Остановить дальнейшее использование. Повторного включения нет; при необходимости добавьте новый ключ."
  ],
  "Stops new evaluation and suppresses pending deliveries where possible.": [
    "Yangi tekshiruvni to‘xtatadi va imkon qadar kutilayotgan yetkazishni to‘xtatadi.",
    "Останавливает новые проверки и по возможности подавляет ожидающие доставки."
  ],
  "Streaming": [
    "Oqimli",
    "Потоковая передача"
  ],
  "Streaming delivery was partial or interrupted. Provider usage may differ from tokens delivered to the client.": [
    "Oqim qisman yoki uzilgan. Provayder foydalanishi mijozga yetkazilgan tokenlardan farq qilishi mumkin.",
    "Поток доставлен частично или прерван. Использование провайдера может отличаться от токенов, доставленных клиенту."
  ],
  "Succeeded": [
    "Muvaffaqiyatli",
    "Успешно"
  ],
  "Supplier spend is catalog-estimated, not an invoice guarantee. Your wallet pays the published BYOK platform fee, not this external spend. Unknown usage can retain holds.": [
    "Provayder sarfi katalog bo‘yicha taxmin, hisob-faktura kafolati emas. Hamyoningiz tashqi sarfni emas, e’lon qilingan BYOK platforma haqini to‘laydi. Noma’lum foydalanish bandni saqlashi mumkin.",
    "Расходы поставщика оцениваются по каталогу, не гарантируют счёт. Кошелёк оплачивает опубликованную плату платформы BYOK, не внешние расходы. Неизвестное использование может удерживать резервы."
  ],
  "Team": [
    "Jamoa",
    "Команда"
  ],
  "Team access updated.": [
    "Jamoa ruxsati yangilandi.",
    "Доступ команды обновлён."
  ],
  "Team emails, invitations and mutation controls are not available to Developer, Billing viewer or Read-only roles.": [
    "Jamoa pochtalari, takliflar va o‘zgartirish amallari Dasturchi, Hisob-kitob kuzatuvchisi yoki Faqat o‘qish uchun mavjud emas.",
    "Почты команды, приглашения и изменения недоступны Разработчику, Наблюдателю биллинга и Только чтение."
  ],
  "Telegram requires private-chat proof. Webhook Active means HTTPS/DNS validation, not endpoint ownership or successful delivery. One destination per channel.": [
    "Telegram shaxsiy chat tasdig‘ini talab qiladi. Faol webhook HTTPS/DNS tekshiruvini bildiradi, egalik yoki yetkazilganlikni emas. Har kanalda bitta manzil.",
    "Telegram требует подтверждения личного чата. Активный webhook означает проверку HTTPS/DNS, не владение или успешную доставку. Один получатель на канал."
  ],
  "Test credential": [
    "Kalitni tekshirish",
    "Проверить ключ"
  ],
  "The API rejected these values. Check the fields and configured ceilings.": [
    "API qiymatlarni rad etdi. Maydon va chegaralarni tekshiring.",
    "API отклонил значения. Проверьте поля и настроенные пределы."
  ],
  "The BYOK provider conflicts with the pin or project provider restrictions.": [
    "BYOK provayderi mahkamlash yoki loyiha provayder chekloviga zid.",
    "Провайдер BYOK конфликтует с фиксацией или ограничениями провайдеров проекта."
  ],
  "The BYOK provider has no published mapping for this model.": [
    "Bu model uchun BYOK provayderining e’lon qilingan mosligi yo‘q.",
    "Для модели нет опубликованного сопоставления провайдера BYOK."
  ],
  "The connection was lost. Refresh before retrying; the operation may already have committed.": [
    "Ulanish uzildi. Qayta urinishdan oldin yangilang; amal allaqachon yozilgan bo‘lishi mumkin.",
    "Соединение потеряно. Обновите перед повтором; операция уже могла быть зафиксирована."
  ],
  "The credential changed during this operation or is disabled. Refresh before retrying; no automatic retry was made.": [
    "Kalit amal davomida o‘zgardi yoki o‘chirilgan. Qayta urinishdan avval yangilang; avtomatik urinish bo‘lmadi.",
    "Ключ изменился во время операции или отключён. Обновите перед повтором; автоматического повтора не было."
  ],
  "The credential does not allow this canonical model.": [
    "Kalit bu kanonik modelga ruxsat bermaydi.",
    "Ключ не разрешает эту каноническую модель."
  ],
  "The credited funds were reversed and payment evidence still needs investigation. Contact support with the payment reference.": [
    "Kredit mablag‘i qaytarib olindi va to‘lov dalilini yana tekshirish kerak. To‘lov raqami bilan yordam xizmatiga murojaat qiling.",
    "Зачисленные средства отменены, а сведения о платеже требуют расследования. Обратитесь в поддержку с номером платежа."
  ],
  "The old secret stops working immediately after commit, with no overlap. Update your application with the show-once replacement. Identity, permissions and budgets remain unchanged.": [
    "Eski kalit yozish tugashi bilan darhol to‘xtaydi, ustma-ust muddat yo‘q. Ilovani bir marta ko‘rsatilgan yangi kalit bilan yangilang. ID, ruxsat va byudjet o‘zgarmaydi.",
    "Старый секрет перестаёт работать сразу после фиксации, без перекрытия. Обновите приложение одноразово показанной заменой. ID, разрешения и бюджеты не меняются."
  ],
  "The resource changed or has active reservations. Refresh and retry after in-flight work finishes; no automatic retry was made.": [
    "Resurs o‘zgargan yoki faol bandlari bor. Yangilang va faol ish tugagach takrorlang; avtomatik urinish bo‘lmadi.",
    "Ресурс изменился или имеет активные резервы. Обновите и повторите после завершения текущей работы; автоматического повтора не было."
  ],
  "The returned link is not a valid official Telegram URL.": [
    "Qaytgan havola rasmiy Telegram URL emas.",
    "Возвращённая ссылка не является корректным официальным URL Telegram."
  ],
  "The service could not confirm the operation. Refresh before retrying; it may already have committed.": [
    "Xizmat amalni tasdiqlay olmadi. Takrorlashdan avval yangilang; allaqachon yozilgan bo‘lishi mumkin.",
    "Сервис не подтвердил операцию. Обновите перед повтором; она уже могла быть зафиксирована."
  ],
  "The status below reflects your wallet, not a provider statement. Reconciliation details are reviewed by support.": [
    "Quyidagi holat provayder hisoboti emas, hamyoningizni aks ettiradi. Hisoblarni solishtirish tafsilotini yordam xizmati ko‘radi.",
    "Статус ниже отражает кошелёк, не выписку провайдера. Детали сверки проверяет поддержка."
  ],
  "The token is sent only by email, never returned here. Pending means issued, not proof of delivery. Expired invitations can be replaced by a new invite; pending duplicates return a conflict. Cancel/resend is not supported by the current API.": [
    "Token faqat pochtaga yuboriladi, bu yerda qaytmaydi. Kutilmoqda — yaratilgan, yetkazilganlik isboti emas. Eskirgan taklif o‘rniga yangi beriladi; kutilayotgan dubl ziddiyat beradi. Joriy API bekor/qayta yuborishni qo‘llamaydi.",
    "Токен отправляется только по почте, здесь не возвращается. Ожидание означает выпуск, не доставку. Истёкшее приглашение можно заменить новым; ожидающие дубли вызывают конфликт. Отмена/повторная отправка текущим API не поддерживаются."
  ],
  "This amount exceeds exact browser editing precision. Use an exact-integer API client instead.": [
    "Miqdor brauzerning aniq tahrir chegarasidan oshdi. Aniq butun sonli API mijozidan foydalaning.",
    "Сумма превышает точность редактирования браузера. Используйте API-клиент с точными целыми числами."
  ],
  "This channel is not configured or is unavailable. Ask the operator; no live connection is confirmed.": [
    "Kanal sozlanmagan yoki ishlamayapti. Operatorga murojaat qiling; haqiqiy ulanish tasdiqlanmagan.",
    "Канал не настроен или недоступен. Обратитесь к оператору; реальное подключение не подтверждено."
  ],
  "This credential is disabled.": [
    "Bu kalit o‘chirilgan.",
    "Этот ключ отключён."
  ],
  "This invitation is invalid, expired, already used, revoked, or does not match your verified email. Ask an Owner/Admin for a new invitation.": [
    "Taklif noto‘g‘ri, eskirgan, ishlatilgan, bekor qilingan yoki tasdiqlangan pochtangizga mos emas. Ega/Administratordan yangi taklif so‘rang.",
    "Приглашение недействительно, истекло, использовано, отозвано или не совпадает с подтверждённой почтой. Попросите новое у Владельца/Админа."
  ],
  "This is permanent: the project cannot be restored. Its keys stop admitting new requests. Existing financial and usage history is retained.": [
    "Doimiy: loyihani tiklab bo‘lmaydi. Kalitlari yangi so‘rovni qabul qilmaydi. Moliyaviy va foydalanish tarixi saqlanadi.",
    "Это навсегда: проект нельзя восстановить. Его ключи не допускают новые запросы. Финансовая история и использование сохраняются."
  ],
  "This key will admit new requests again, subject to expiry, project restrictions, budgets and limits.": [
    "Kalit muddat, loyiha cheklovi, byudjet va limitlar doirasida yangi so‘rovni yana qabul qiladi.",
    "Ключ снова допускает новые запросы с учётом срока, ограничений проекта, бюджетов и лимитов."
  ],
  "This project has no explicit grant for the credential.": [
    "Bu loyihaga kalit uchun aniq ruxsat berilmagan.",
    "У проекта нет явного разрешения на этот ключ."
  ],
  "This project is archived. New inference and settings edits are unavailable; financial history is retained.": [
    "Loyiha arxivlangan. Yangi inferensiya va sozlama tahriri yo‘q; moliyaviy tarix saqlanadi.",
    "Проект архивирован. Новые запросы и редактирование настроек недоступны; финансовая история сохраняется."
  ],
  "This project is not available.": [
    "Bu loyiha mavjud emas.",
    "Этот проект недоступен."
  ],
  "This request is unavailable or outside your organization.": [
    "So‘rov mavjud emas yoki tashkilotingizdan tashqarida.",
    "Запрос недоступен или вне вашей организации."
  ],
  "This resource is unavailable in the current workspace. Refresh the list.": [
    "Resurs joriy ish maydonida mavjud emas. Ro‘yxatni yangilang.",
    "Ресурс недоступен в текущем рабочем пространстве. Обновите список."
  ],
  "This revokes organization membership and removes every project grant. History remains. Rejoining requires a fresh invitation.": [
    "Tashkilot a’zoligi bekor qilinadi va barcha loyiha ruxsati olib tashlanadi. Tarix qoladi. Qaytish uchun yangi taklif kerak.",
    "Отзывает членство организации и удаляет все разрешения проектов. История остаётся. Для возврата нужно новое приглашение."
  ],
  "This service is temporarily unavailable. Please try again later.": [
    "Xizmat vaqtincha ishlamayapti. Keyinroq urinib ko‘ring.",
    "Сервис временно недоступен. Повторите позже."
  ],
  "This token is invalid, expired, or already used. Request a new one and try again.": [
    "Token noto‘g‘ri, eskirgan yoki ishlatilgan. Yangisini so‘rab takrorlang.",
    "Токен недействителен, истёк или использован. Запросите новый и повторите."
  ],
  "Threshold (%)": [
    "Chegara (%)",
    "Порог (%)"
  ],
  "Threshold rules": [
    "Chegara qoidalari",
    "Пороговые правила"
  ],
  "To (UTC)": [
    "Tugash (UTC)",
    "До (UTC)"
  ],
  "Tokens · input / output": [
    "Tokenlar · kirish / chiqish",
    "Токены · ввод / вывод"
  ],
  "Too many attempts. Wait a few minutes before trying again.": [
    "Urinishlar ko‘p. Takrorlashdan avval bir necha daqiqa kuting.",
    "Слишком много попыток. Подождите несколько минут перед повтором."
  ],
  "Too many requests. Wait before trying again.": [
    "So‘rovlar ko‘p. Qayta urinishdan avval kuting.",
    "Слишком много запросов. Подождите перед повтором."
  ],
  "Top models": [
    "Asosiy modellar",
    "Ведущие модели"
  ],
  "Top providers": [
    "Asosiy provayderlar",
    "Ведущие провайдеры"
  ],
  "Top up in UZS. Your locked quote determines the credited USD amount.": [
    "UZS da to‘ldiring. Qat’iy belgilangan taklif USD kredit miqdorini belgilaydi.",
    "Пополняйте в UZS. Зафиксированный расчёт определяет зачисляемую сумму USD."
  ],
  "Top up with Payme or CLICK": [
    "Payme yoki CLICK bilan to‘ldiring",
    "Пополните через Payme или CLICK"
  ],
  "Trace": [
    "Trassa",
    "Трассировка"
  ],
  "Triggered": [
    "Ishga tushgan",
    "Сработало"
  ],
  "USAGE INTELLIGENCE": [
    "FOYDALANISH TAHLILI",
    "АНАЛИТИКА ИСПОЛЬЗОВАНИЯ"
  ],
  "USD credits": [
    "USD kreditlari",
    "Кредиты USD"
  ],
  "UTC dates; settled charges exclude pending usage. Analytics refreshes every 10 seconds while visible.": [
    "UTC sanalari; yakuniy to‘lovlarga kutilayotgan usage kirmaydi. Sahifa ochiq bo‘lsa tahlil har 10 soniyada yangilanadi.",
    "Даты UTC; итоговые списания исключают ожидающее использование. Аналитика обновляется каждые 10 секунд, пока видима."
  ],
  "Unavailable": [
    "Mavjud emas",
    "Недоступно"
  ],
  "Unavailable — quota, service, network or verification deadline prevented a conclusive test. No automatic retry.": [
    "Mavjud emas — kvota, xizmat, tarmoq yoki tekshiruv muddati aniq natijaga yo‘l bermadi. Avtomatik qayta urinish yo‘q.",
    "Недоступно — квота, сервис, сеть или срок проверки помешали однозначному результату. Автоматического повтора нет."
  ],
  "Uncapped": [
    "Cheklanmagan",
    "Без лимита"
  ],
  "Under review": [
    "Tekshiruvda",
    "На проверке"
  ],
  "Unknown": [
    "Noma’lum",
    "Неизвестно"
  ],
  "Unknown / unsettled": [
    "Noma’lum / yakunlanmagan",
    "Неизвестно / не урегулировано"
  ],
  "Unrestricted (null)": [
    "Cheklanmagan (null)",
    "Без ограничений (null)"
  ],
  "Unrestricted provider models": [
    "Provayder modellari cheklanmagan",
    "Без ограничений моделей провайдера"
  ],
  "Unrouted": [
    "Yo‘naltirilmagan",
    "Без маршрута"
  ],
  "Unsafe budget precision.": [
    "Byudjet aniqligi xavfsiz emas.",
    "Небезопасная точность бюджета."
  ],
  "Up to 6 decimal places. Saving replaces this period only. Budget and request-limit updates are independent; Owner/Admin billing permission is required.": [
    "6 kasr xonasigacha. Saqlash faqat shu davrni almashtiradi. Byudjet va so‘rov limiti alohida; Ega/Administrator hisob-kitob ruxsati kerak.",
    "До 6 знаков после точки. Сохранение заменяет только этот период. Бюджет и лимиты запросов независимы; нужны финансовые права Владельца/Админа."
  ],
  "Up to two explicit canonical alternatives; same-model candidates are considered before these alternatives. The provider pin constrains the primary model only; explicit alternatives may use other permitted providers. No project default enables this.": [
    "Ko‘pi bilan ikkita aniq kanonik muqobil; avval bir xil model nomzodlari. Provayder mahkamlash faqat asosiy modelga; muqobil boshqa ruxsatli provayderga o‘tishi mumkin. Loyiha standarti buni yoqmaydi.",
    "До двух явных канонических альтернатив; сначала кандидаты той же модели. Фиксация провайдера ограничивает только основную модель; альтернативы могут использовать других разрешённых провайдеров. Настройки проекта это не включают."
  ],
  "Updating usage ·": [
    "Foydalanish yangilanmoqda ·",
    "Обновление использования ·"
  ],
  "Updating…": [
    "Yangilanmoqda…",
    "Обновление…"
  ],
  "Upstream ID:": [
    "Yuqori provayder ID:",
    "ID провайдера:"
  ],
  "Usage evidence": [
    "Foydalanish dalili",
    "Сведения об использовании"
  ],
  "Use a non-negative USD amount with up to six decimals; no exponent or commas.": [
    "Olti kasr xonasigacha manfiy bo‘lmagan USD kiriting; eksponent yoki vergulsiz.",
    "Введите неотрицательную сумму USD до шести знаков после точки; без экспоненты или запятых."
  ],
  "Use a percentage with at most two decimals.": [
    "Ko‘pi bilan ikki kasr xonali foiz kiriting.",
    "Введите процент не более чем с двумя знаками после точки."
  ],
  "Use active canonical model codes for this provider. Project and API-key restrictions still intersect. Saving replaces this restriction group.": [
    "Bu provayderning faol kanonik model kodlarini ishlating. Loyiha va API kalit cheklovlari birga qo‘llanadi. Saqlash shu guruhni almashtiradi.",
    "Используйте активные канонические коды моделей провайдера. Ограничения проекта и API-ключа пересекаются. Сохранение заменяет эту группу."
  ],
  "Use the one-time token from your email. A used or expired token cannot be reused.": [
    "Pochtadagi bir martalik tokenni ishlating. Ishlatilgan yoki eskirgan token qayta ishlamaydi.",
    "Используйте одноразовый токен из почты. Использованный или истёкший токен нельзя применить снова."
  ],
  "Use the one-time token from your invitation email. Sign in with that exact, verified email address first.": [
    "Taklif pochtasidagi bir martalik tokenni ishlating. Avval aynan shu tasdiqlangan pochta bilan kiring.",
    "Используйте одноразовый токен из письма-приглашения. Сначала войдите с этим точным подтверждённым адресом."
  ],
  "Use up to 128 canonical codes: letters, digits, -, _, ., :; 200 characters each. No wildcards or provider/model aliases.": [
    "128 tagacha kanonik kod: harf, raqam, -, _, ., :; har biri 200 belgigacha. Wildcard yoki provayder/model taxallusisiz.",
    "До 128 канонических кодов: буквы, цифры, -, _, ., :; до 200 символов каждый. Без wildcard или псевдонимов провайдеров/моделей."
  ],
  "Valid": [
    "To‘g‘ri",
    "Действительно"
  ],
  "Valid until": [
    "Amal qiladi:",
    "Действует до"
  ],
  "Valid — model-list authentication accepted; not a quota, model-access or billing guarantee.": [
    "To‘g‘ri — model ro‘yxati autentifikatsiyasi qabul qilindi; kvota, model ruxsati yoki hisob-kitob kafolati emas.",
    "Действительно — аутентификация списка моделей принята; не гарантия квоты, доступа к моделям или биллинга."
  ],
  "Validated": [
    "Tekshirilgan",
    "Проверено"
  ],
  "Verification does not reset inference health.": [
    "Tekshiruv inferensiya sog‘lig‘ini tiklamaydi.",
    "Проверка не сбрасывает состояние инференса."
  ],
  "Verification finished.": [
    "Tekshiruv tugadi.",
    "Проверка завершена."
  ],
  "Verification token": [
    "Tasdiqlash tokeni",
    "Токен подтверждения"
  ],
  "Verified": [
    "Tasdiqlangan",
    "Подтверждено"
  ],
  "Verified token evidence and settled charges across your selected window.": [
    "Tanlangan davrdagi tasdiqlangan token dalili va yakuniy to‘lovlar.",
    "Подтверждённые сведения о токенах и итоговые списания выбранного периода."
  ],
  "Verified tokens": [
    "Tasdiqlangan tokenlar",
    "Подтверждённые токены"
  ],
  "Verify email": [
    "Pochtani tasdiqlash",
    "Подтвердить почту"
  ],
  "Verify your email": [
    "Pochtangizni tasdiqlang",
    "Подтвердите почту"
  ],
  "Verify your email first": [
    "Avval pochtangizni tasdiqlang",
    "Сначала подтвердите почту"
  ],
  "WELCOME TO UZLLM": [
    "UZLLM GA XUSH KELIBSIZ",
    "ДОБРО ПОЖАЛОВАТЬ В UZLLM"
  ],
  "WORKSPACE": [
    "ISH MAYDONI",
    "РАБОЧЕЕ ПРОСТРАНСТВО"
  ],
  "WORKSPACE CONTROLS ·": [
    "ISH MAYDONI BOSHQARUVI ·",
    "УПРАВЛЕНИЕ ПРОСТРАНСТВОМ ·"
  ],
  "WORKSPACE INVITATION": [
    "ISH MAYDONIGA TAKLIF",
    "ПРИГЛАШЕНИЕ В ПРОСТРАНСТВО"
  ],
  "Waiting since {p0}.": [
    "{p0} dan beri kutilmoqda.",
    "Ожидание с {p0}."
  ],
  "Wallet credit {p0}{p1}": [
    "Hamyon krediti {p0}{p1}",
    "Кредит кошелька {p0}{p1}"
  ],
  "Wallet refund": [
    "Hamyon qaytarishi",
    "Возврат в кошелёк"
  ],
  "Wallet-credit refunds": [
    "Hamyonga kredit qaytarishlari",
    "Возвраты кредита в кошелёк"
  ],
  "Webhook HTTPS URL": [
    "Webhook HTTPS URL",
    "HTTPS URL webhook"
  ],
  "Weekly": [
    "Haftalik",
    "Еженедельно"
  ],
  "When": [
    "Vaqti",
    "Когда"
  ],
  "Where alerts go": [
    "Ogohlantirish manzillari",
    "Получатели оповещений"
  ],
  "Workspace not found": [
    "Ish maydoni topilmadi",
    "Рабочее пространство не найдено"
  ],
  "YOUR WORKSPACE": [
    "ISH MAYDONINGIZ",
    "ВАШЕ ПРОСТРАНСТВО"
  ],
  "You are": [
    "Siz",
    "Вы"
  ],
  "You are changing your own access and may lose this workspace immediately.": [
    "O‘z ruxsatingizni o‘zgartiryapsiz va darhol bu ish maydonidan ayrilishingiz mumkin.",
    "Вы меняете собственный доступ и можете немедленно потерять это пространство."
  ],
  "You do not have access to this project.": [
    "Bu loyihaga ruxsatingiz yo‘q.",
    "У вас нет доступа к этому проекту."
  ],
  "You do not have access to this workspace.": [
    "Bu ish maydoniga ruxsatingiz yo‘q.",
    "У вас нет доступа к этому пространству."
  ],
  "You pay": [
    "Siz to‘laysiz",
    "Вы платите"
  ],
  "Your current membership cannot manage this team. Refresh or ask an organization owner.": [
    "Joriy a’zolik jamoani boshqara olmaydi. Yangilang yoki tashkilot egasiga murojaat qiling.",
    "Ваше членство не позволяет управлять командой. Обновите или обратитесь к владельцу."
  ],
  "Your new API key": [
    "Yangi API kalitingiz",
    "Ваш новый API-ключ"
  ],
  "Your projects": [
    "Loyihalaringiz",
    "Ваши проекты"
  ],
  "Your providers. Your credentials. Explicit access for every project.": [
    "Provayderingiz. Kalitingiz. Har loyihaga aniq ruxsat.",
    "Ваши провайдеры. Ваши ключи. Явный доступ каждому проекту."
  ],
  "Your session expired. Sign in again before saving.": [
    "Sessiya tugagan. Saqlashdan avval qayta kiring.",
    "Сессия истекла. Войдите снова перед сохранением."
  ],
  "Your token is not saved or forwarded. After signing in, return here and paste it from your email.": [
    "Token saqlanmaydi yoki uzatilmaydi. Kirgach qaytib, pochtangizdan kiriting.",
    "Токен не сохраняется и не пересылается. После входа вернитесь и вставьте его из почты."
  ],
  "aggregation buckets pending. Unknown usage is not zero.": [
    "jamlash guruhlari kutilmoqda. Noma’lum foydalanish nol emas.",
    "групп агрегации ожидается. Неизвестное использование — не ноль."
  ],
  "checkout ↗": [
    "to‘lov oynasi ↗",
    "оплата ↗"
  ],
  "codes": [
    "kodlari",
    "коды"
  ],
  "completed": [
    "yakunlangan",
    "завершено"
  ],
  "context": [
    "kontekst",
    "контекст"
  ],
  "credited": [
    "kredit tushdi",
    "зачислено"
  ],
  "e.g. Production": [
    "masalan, Production",
    "например, Production"
  ],
  "e.g. anthropic": [
    "masalan, anthropic",
    "например, anthropic"
  ],
  "eligible provider": [
    "mos provayder",
    "допустимый провайдер"
  ],
  "errors": [
    "xatolar",
    "ошибок"
  ],
  "explicit access to": [
    "aniq ruxsat:",
    "явный доступ к"
  ],
  "explicit project grants": [
    "aniq loyiha ruxsatlari",
    "явных разрешений проектов"
  ],
  "explicit request override": [
    "aniq so‘rov almashtirishi",
    "явное переопределение запроса"
  ],
  "from capture when enabled · 60–10080 minutes permitted (one hour–seven days).": [
    "yoqilganda qayd etishdan boshlab · 60–10080 daqiqa mumkin (bir soat–yetti kun).",
    "с момента записи при включении · разрешено 60–10080 минут (час–семь дней)."
  ],
  "hour(s) pending.": [
    "soat kutilmoqda.",
    "часов ожидается."
  ],
  "in": [
    "ichida",
    "в"
  ],
  "in this organization. This affects future admission only.": [
    "ushbu tashkilotda. Faqat keyingi qabulga ta’sir qiladi.",
    "в этой организации. Влияет только на будущий допуск."
  ],
  "input ·": [
    "kirish ·",
    "ввод ·"
  ],
  "is added per request. Actual charge depends on the chosen provider and measured tokens; wallet admission may reserve more. BYOK uses a separate fee policy.": [
    "har so‘rovga qo‘shiladi. Haqiqiy to‘lov tanlangan provayder va o‘lchangan tokenga bog‘liq; hamyon qabulda ko‘proq band qilishi mumkin. BYOK uchun alohida haq siyosati.",
    "добавляется к запросу. Итог зависит от провайдера и измеренных токенов; допуск кошелька может резервировать больше. BYOK использует отдельную плату."
  ],
  "keys · metadata only": [
    "kalit · faqat metadata",
    "ключей · только метаданные"
  ],
  "minutes": [
    "daqiqa",
    "минут"
  ],
  "output": [
    "chiqish",
    "вывод"
  ],
  "priority": [
    "ustuvorlik",
    "приоритет"
  ],
  "project default / deterministic inheritance": [
    "loyiha standarti / belgilangan meros",
    "настройки проекта / детерминированное наследование"
  ],
  "provider": [
    "provayder",
    "провайдер"
  ],
  "requests": [
    "so‘rov",
    "запросов"
  ],
  "requests unsettled ·": [
    "so‘rov yakunlanmagan ·",
    "запросов не урегулировано ·"
  ],
  "rule": [
    "qoida",
    "правило"
  ],
  "settled charge": [
    "yakuniy to‘lov",
    "итоговое списание"
  ],
  "total": [
    "jami",
    "всего"
  ],
  "usage": [
    "foydalanish",
    "использование"
  ],
  "verified tokens": [
    "tasdiqlangan token",
    "подтверждённых токенов"
  ],
  "{p0} budget utilization": [
    "{p0} byudjet sarfi",
    "Использование бюджета {p0}"
  ],
  "{p0} explicit project grants": [
    "{p0} aniq loyiha ruxsatlari",
    "{p0} явных разрешений проектов"
  ],
  "{p0} loaded": [
    "{p0} yuklandi",
    "{p0} загружено"
  ],
  "{p0} ms": [
    "{p0} ms",
    "{p0} мс"
  ],
  "· Cached": [
    "· Keshlangan",
    "· Кеш"
  ],
  "· Expires": [
    "· Amal muddati",
    "· Истекает"
  ],
  "· Joined": [
    "· Qo‘shilgan",
    "· Присоединился"
  ],
  "· Output": [
    "· Chiqish",
    "· Вывод"
  ],
  "· Reasoning": [
    "· Fikrlash",
    "· Рассуждение"
  ],
  "· Recheck after {p0}": [
    "· {p0} dan so‘ng tekshiring",
    "· Проверить после {p0}"
  ],
  "· Settlement": [
    "· Yakunlash",
    "· Урегулирование"
  ],
  "· Stream": [
    "· Oqim",
    "· Поток"
  ],
  "· Streaming": [
    "· Oqimli",
    "· Потоковая передача"
  ],
  "· USD per 1M tokens · published status is not a live health guarantee.": [
    "· 1 mln token uchun USD · e’lon qilingan holat joriy sog‘lomlik kafolati emas.",
    "· USD за 1 млн токенов · опубликованный статус не гарантирует доступность сейчас."
  ],
  "· accepted": [
    "· qabul qilingan",
    "· принято"
  ],
  "· ceiling": [
    "· chegara",
    "· предел"
  ],
  "· expires": [
    "· muddati",
    "· истекает"
  ],
  "· net": [
    "· sof",
    "· чистое"
  ],
  "· observed": [
    "· kuzatilgan",
    "· наблюдалось"
  ],
  "· rule": [
    "· qoida",
    "· правило"
  ],
  "· select a project": [
    "· loyiha tanlang",
    "· выберите проект"
  ],
  "Display preferences": [
    "Ko‘rinish sozlamalari",
    "Настройки отображения"
  ],
  "Language": [
    "Til",
    "Язык"
  ],
  "Display time zone": [
    "Ko‘rsatish vaqt mintaqasi",
    "Часовой пояс отображения"
  ],
  "Local time": [
    "Mahalliy vaqt",
    "Местное время"
  ]
};
