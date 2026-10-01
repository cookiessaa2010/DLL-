KAI TOR UNLIMITED COMPANIONS v1.3.15.09 - FULL LIVE TEST

Этот пакет сделан специально для live-теста кнопки массового призыва спутников.

ПОЧЕМУ ПРЕДЫДУЩАЯ СБОРКА БЫЛА СЕРОЙ В ЛАУНЧЕРЕ
1. Одновременно существовали Workshop v1.3.15.5 и локальная тестовая копия с тем же Module ID.
2. Для новой кнопки нужен UIExtenderEx, а он не был установлен как отдельный Bannerlord-модуль.

ЧТО ДЕЛАЕТ ЭТОТ ПАКЕТ
- содержит Kai TOR Unlimited Companions v1.3.15.09;
- содержит официальный Bannerlord.UIExtenderEx v2.13.3;
- автоматически убирает локальный дубликат Kai TOR;
- если найден Workshop item 3809043415, делает его резервную копию и временно ставит тестовую сборку прямо в его папку;
- устанавливает/обновляет UIExtenderEx;
- резервные копии складывает в %LOCALAPPDATA%\KaiTOR\UnlimitedCompanions\TestBackups.

УСТАНОВКА
1. Полностью закрой Bannerlord и лаунчер.
2. Распакуй весь архив.
3. Запусти INSTALL_FULL_TEST.bat.
4. Открой лаунчер.
5. Включи:
   Bannerlord.Harmony
   Bannerlord.UIExtenderEx
   Kai TOR Unlimited Companions
   затем TOR-модули в обычном порядке.
6. В списке должна остаться только одна строка Kai TOR Unlimited Companions v1.3.15.9.

ФУНКЦИИ v1.3.15.09
- итоговый лимит спутников = максимум из расчёта Bannerlord/TOR и текущего числа спутников + 200;
- кнопка массового призыва на экране Клан -> Члены;
- используются штатные проверки доступности Bannerlord;
- занятые/пленные/губернаторы/лидеры отрядов не затрагиваются;
- используется штатный delayed recall, а не грубая телепортация;
- диагностический лог:
  Documents\Mount and Blade II Bannerlord\Configs\KaiTOR_UnlimitedCompanions.log

UIExtenderEx
В комплект включена официальная сборка BUTR Bannerlord.UIExtenderEx v2.13.3.
Проект: https://github.com/BUTR/Bannerlord.UIExtenderEx
Лицензия UIExtenderEx: GNU Lesser General Public License v3.0 (LGPL-3.0).\nОфициальный файл LICENSE включён в полный пакет; UIExtenderEx распространяется без изменений.
