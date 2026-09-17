KaiTOR Dawi Women v0.2.0 — ПЕРВЫЙ LIVE TEST
================================================

Эта версия НЕ создаёт дворфиек автоматически.
Она предназначена только для проверки реального female-dwarf rig на TOR 1.3.15.

УСТАНОВКА
1. Установи папки KaiTOR_Diplomacy и KaiTOR_DawiWomen в Bannerlord\Modules.
2. В лаунчере KaiTOR_DawiWomen должен идти ПОСЛЕ TOR и ПОСЛЕ KaiTOR_Diplomacy.
3. НЕ запускай сохранение до подготовки ассетов.
4. Перетащи свою локальную папку LOTRLOME_Armory на:
      KaiTOR_DawiWomen\SETUP-DAWI-ASSETS.cmd
   Скрипт НЕ читает содержимое TPAC побайтно. Он находит известные файлы по имени,
   копирует локальный Assets\Race Test и берёт female skin/action set из твоей версии Armory.
5. Дождись строки READY_FOR_MANUAL_RIG_TEST и полностью перезапусти Bannerlord.

ПЕРВЫЙ ТЕСТ
В игровой консоли:
  kaitor_dawi_women.status

Нужно увидеть:
  READY
  Population behavior: REGISTERED
  Automatic population: OFF - MANUAL RIG TEST

Только после этого:
  kaitor_dawi_women.spawn_test

Команда создаёт РОВНО ОДНУ женщину-Dawi в подходящем NPC-клане.
Проверь её в энциклопедии и на модели. Затем сохранись и перезагрузи сохранение.

ВАЖНО
- Не включай автоматическое население: в этой сборке оно жёстко OFF.
- Стабильная KaiTOR_Diplomacy v0.5.1 этой тестовой веткой не изменена.
- Если будет CTD/T-pose/сломанная геометрия — пришли rgl_log после этого запуска.
- Файл ModuleData\kaitor_dawi_assets_ready.flag создаётся только после успешной локальной проверки ассетов.
