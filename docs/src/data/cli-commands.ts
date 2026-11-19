import type { CommandEntry } from './cli-surface';

export const COMMAND_SURFACE: CommandEntry[] = [
  {
    id: 'help',
    group: 'inspect',
    syntax: 'nbtcli help [command]',
    tier: 'safe',
    requiredArgs: {
      en: 'Optional root command name (for subcommands, run <command> <subcommand> --help)',
      'zh-tw': '可選填主指令名稱（查詢子命令請改用 <指令> <子命令> --help）',
    },
    summary: {
      en: 'Shows available commands and accepted options.',
      'zh-tw': '顯示所有可用的指令與支援參數清單。',
    },
    resultAndNext: {
      en: 'Use whenever you want to double-check a flag name.',
      'zh-tw': '忘記參數怎麼拼時隨時查一下。',
    },
  },
  {
    id: 'version',
    group: 'inspect',
    syntax: 'nbtcli version',
    tier: 'safe',
    requiredArgs: {
      en: 'None',
      'zh-tw': '無',
    },
    summary: {
      en: 'Prints the tool name (nbtcli) and version (1.0.0).',
      'zh-tw': '顯示工具名稱（nbtcli）與目前版本號（1.0.0）。',
    },
    resultAndNext: {
      en: 'Quick way to verify the tool runs right after unzipping.',
      'zh-tw': '解壓縮後最快確認程式能正常啟動的方法。',
    },
  },
  {
    id: 'get',
    group: 'inspect',
    syntax: 'nbtcli get <world-or-file>',
    tier: 'safe',
    requiredArgs: {
      en: 'World folder or single NBT / JSON / ZIP file path',
      'zh-tw': '世界資料夾，或單一 NBT / JSON / ZIP 檔案路徑',
    },
    summary: {
      en: 'Reads a specific value from level.dat, a chunk (.mca), a datapack JSON, or a ZIP archive without modifying anything.',
      'zh-tw': '讀取 level.dat、區塊檔（.mca）、資料包 JSON 或 ZIP 內的指定資料，完全不改檔。',
    },
    resultAndNext: {
      en: 'Add --types if you plan to edit that value later with set.',
      'zh-tw': '如果等一下要用 set 修改這個值，記得加上 --types 保留正確型別。',
    },
  },
  {
    id: 'list',
    group: 'inspect',
    syntax: 'nbtcli list <world-or-file>',
    tier: 'safe',
    requiredArgs: {
      en: 'World folder, NBT file, or region (.mca) file',
      'zh-tw': '世界資料夾、NBT 檔案或區塊檔（.mca）',
    },
    summary: {
      en: 'Lists dimensions in a world, generated chunks inside a region file, or child fields inside a tag.',
      'zh-tw': '列出世界包含哪些維度、區塊檔裡有哪些區塊座標，或某個欄位底下有哪些子項目。',
    },
    resultAndNext: {
      en: 'Great first step to see what is inside a world before running get or scan.',
      'zh-tw': '最適合在執行 get 或 scan 之前，先看清楚地圖裡有哪些東西。',
    },
  },
  {
    id: 'search',
    group: 'inspect',
    syntax: 'nbtcli search <world-or-file>',
    tier: 'safe',
    requiredArgs: {
      en: 'Target path plus at least one filter (--query, --name, or --type)',
      'zh-tw': '目標路徑，加上至少一個搜尋條件（--query、--name 或 --type）',
    },
    summary: {
      en: 'Searches inside an NBT or JSON file by value text, field name, or NBT data type.',
      'zh-tw': '依文字內容、欄位名稱或資料型別搜尋存檔中的資料。',
    },
    resultAndNext: {
      en: 'Shows matching paths and previews so you can pinpoint values with get.',
      'zh-tw': '會回傳符合項目的路徑與內容預覽，方便再拿去用 get 精準讀取。',
    },
  },
  {
    id: 'scan',
    group: 'scan',
    syntax: 'nbtcli scan <world>',
    tier: 'safe',
    requiredArgs: {
      en: 'World folder path',
      'zh-tw': '世界資料夾路徑',
    },
    summary: {
      en: 'Finds translatable text across signs, books, items, entities, command blocks, scoreboards, and datapacks.',
      'zh-tw': '掃描整張地圖裡告示牌、書本、物品、實體、指令方塊、計分板與資料包的所有文字。',
    },
    resultAndNext: {
      en: 'Use --summary for a quick count, then run extract or translate start.',
      'zh-tw': '加上 --summary 可看快速統計，接著就能進行 extract 或 translate start。',
    },
  },
  {
    id: 'extract',
    group: 'scan',
    syntax: 'nbtcli extract <world>',
    tier: 'safe',
    requiredArgs: {
      en: 'World folder path',
      'zh-tw': '世界資料夾路徑',
    },
    summary: {
      en: 'Exports all discovered world text and where each line lives, with optional deduplication (--unique) or regex filtering.',
      'zh-tw': '把地圖裡的文字和出現位置匯出出來，可加 --unique 去除重複文字預覽或用關鍵字篩選。',
    },
    resultAndNext: {
      en: 'Use --output <file> to save the list to a folder outside the world.',
      'zh-tw': '搭配 --output <檔案> 可存到世界資料夾外的檔案慢慢看。',
    },
  },
  {
    id: 'validate',
    group: 'scan',
    syntax: 'nbtcli validate <world>',
    tier: 'safe',
    requiredArgs: {
      en: 'World folder path',
      'zh-tw': '世界資料夾路徑',
    },
    summary: {
      en: 'Checks whether world files, regions, and datapack JSON can be read cleanly (use --quick for a fast header check).',
      'zh-tw': '檢查存檔、區塊與資料包是否都能正常讀取（加 --quick 可做快速基本檢查）。',
    },
    resultAndNext: {
      en: 'Points out unsupported formats (exit 4) or damaged files (exit 5) before you edit.',
      'zh-tw': '能在動手修改前先幫你抓出不支援的格式（退出碼 4）或損壞的檔案（退出碼 5）。',
    },
  },
  {
    id: 'capabilities',
    group: 'scan',
    syntax: 'nbtcli capabilities',
    tier: 'safe',
    requiredArgs: {
      en: 'None',
      'zh-tw': '無',
    },
    summary: {
      en: 'Lists every supported Minecraft Java Edition version profile and which operations are supported.',
      'zh-tw': '查詢目前支援哪些 Minecraft Java 版存檔，以及各自能讀取、翻譯還是寫入。',
    },
    resultAndNext: {
      en: 'Run this whenever you are unsure if your map version is supported.',
      'zh-tw': '不確定自己的地圖版本支不支援時，先跑這一行最準。',
    },
  },
  {
    id: 'set',
    group: 'edit',
    syntax: 'nbtcli set <world> --path <rel-path> (--value <json> | --value-file <file>)',
    tier: 'preview',
    requiredArgs: {
      en: 'World folder, --path, --pointer (or --segments), and --value / --value-file from get --types',
      'zh-tw': '世界資料夾、--path、--pointer（或 --segments），以及從 get --types 取得的 --value / --value-file',
    },
    summary: {
      en: 'Creates a preview plan to change one NBT value while keeping its exact Minecraft data type. Does not modify the world unless you add --apply --world-offline.',
      'zh-tw': '建立修改單一存檔值的預覽計畫並保留正確型別；除非加上 --apply --world-offline，否則不會動到世界。',
    },
    resultAndNext: {
      en: 'Save the plan with --output and apply it with nbtcli apply --plan ... --world-offline.',
      'zh-tw': '用 --output 存成計畫檔，檢查後再用 nbtcli apply --plan ... --world-offline 套用。',
    },
  },
  {
    id: 'batch',
    group: 'edit',
    syntax: 'nbtcli batch <file-or-dash> --world <world>',
    tier: 'preview',
    requiredArgs: {
      en: 'Operations JSON file (or - for stdin) and --world <world>',
      'zh-tw': '批次操作 JSON 檔（或用 - 從標準輸入讀取）與 --world <世界資料夾>',
    },
    summary: {
      en: 'Previews or applies multiple NBT edits at once from an operations file.',
      'zh-tw': '一次預覽或套用多筆存檔修改（讀取 nbtcli.operations 檔案）。',
    },
    resultAndNext: {
      en: 'Previews by default; save with --output or commit with --apply --world-offline.',
      'zh-tw': '預設只做預覽；可用 --output 存成計畫，或加 --apply --world-offline 離線寫入。',
    },
  },
  {
    id: 'plan',
    group: 'edit',
    syntax: 'nbtcli plan <world> --request <file-or-dash>',
    tier: 'preview',
    requiredArgs: {
      en: 'World folder and --request <operations-file>',
      'zh-tw': '世界資料夾與 --request <批次操作檔>',
    },
    summary: {
      en: 'Builds and checks a reviewable patch plan from an operations file without touching the world.',
      'zh-tw': '將多筆修改請求驗證並打包成可檢查的計畫檔，完全不改動世界。',
    },
    resultAndNext: {
      en: 'Save with --output <plan.json>, inspect it, then apply offline with apply.',
      'zh-tw': '用 --output <plan.json> 存檔檢查後，再交給 apply 離線套用。',
    },
  },
  {
    id: 'apply',
    group: 'edit',
    syntax: 'nbtcli apply <world> --plan <file-or-dash> --world-offline',
    tier: 'write',
    requiredArgs: {
      en: 'World folder, --plan <plan.json>, and --world-offline',
      'zh-tw': '世界資料夾、--plan <計畫檔> 與 --world-offline',
    },
    summary: {
      en: 'Applies a reviewed plan while Minecraft is closed, automatically saving backups outside your world and verifying the written files.',
      'zh-tw': '在關閉 Minecraft 後正式套用計畫檔，寫入前會自動在世界外做好備份，寫完後還會自動驗收。',
    },
    resultAndNext: {
      en: 'Returns a completion receipt. If interrupted (exit 7), run status and recover.',
      'zh-tw': '成功會回傳完成回執；萬一中途被中斷（退出碼 7），請跑 status 與 recover。',
    },
  },
  {
    id: 'status',
    group: 'edit',
    syntax: 'nbtcli status <world> --plan-hash <hash>',
    tier: 'safe',
    requiredArgs: {
      en: 'World folder and --plan-hash <sha256> (plus --state-root if you used a custom one)',
      'zh-tw': '世界資料夾與 --plan-hash <計畫雜湊值>（有自訂 --state-root 也要帶入）',
    },
    summary: {
      en: 'Checks whether an edit finished cleanly, stopped midway, or already rolled back.',
      'zh-tw': '查看某次修改是已經完成、中途卡住，還是已經還原回去。',
    },
    resultAndNext: {
      en: 'Read-only check; review its guidance before running recover.',
      'zh-tw': '純唯讀檢查；確認狀態後再決定要用 recover 續寫還是加 --rollback 還原。',
    },
  },
  {
    id: 'recover',
    group: 'edit',
    syntax: 'nbtcli recover <world> --plan-hash <hash> --world-offline',
    tier: 'write',
    requiredArgs: {
      en: 'World folder, --plan-hash <sha256>, and --world-offline',
      'zh-tw': '世界資料夾、--plan-hash <計畫雜湊值> 與 --world-offline',
    },
    summary: {
      en: 'Finishes an interrupted write, or undoes it completely back to the original files when you add --rollback.',
      'zh-tw': '把中途停掉的寫入安全接續做完，或加上 --rollback 完整還原成修改前的原始檔案。',
    },
    resultAndNext: {
      en: 'Exit 0 means finished forward; exit 8 means cleanly rolled back.',
      'zh-tw': '退出碼 0 代表續寫完成；退出碼 8 代表已完整還原回修改前。',
    },
  },
  {
    id: 'backup',
    group: 'edit',
    syntax: 'nbtcli backup <world> [--suffix <name>]',
    tier: 'safe',
    requiredArgs: {
      en: 'World folder path',
      'zh-tw': '世界資料夾路徑',
    },
    summary: {
      en: 'Saves a verified backup of the whole world (or one --path file) outside your Minecraft save folder.',
      'zh-tw': '在世界資料夾外建立完整備份（也可用 --path 只備份單一檔案），不更動原世界。',
    },
    resultAndNext: {
      en: 'Keep the printed manifestHash (and stateRoot) so you can restore anytime.',
      'zh-tw': '記下輸出裡的 manifestHash（與 stateRoot），之後隨時可用 restore 還原。',
    },
  },
  {
    id: 'restore',
    group: 'edit',
    syntax: 'nbtcli restore <world> --manifest-hash <hash>',
    tier: 'preview',
    requiredArgs: {
      en: 'World folder and --manifest-hash <sha256>',
      'zh-tw': '世界資料夾與 --manifest-hash <備份雜湊值>',
    },
    summary: {
      en: 'Previews or restores world files from a previous backup, even if a file is currently broken or missing.',
      'zh-tw': '從先前的備份預覽或還原世界檔案，就算目前存檔已經損壞或誤刪也能救回。',
    },
    resultAndNext: {
      en: 'Previews by default; save the plan with --output and run apply --plan ... --world-offline.',
      'zh-tw': '預設只做預覽；建議先用 --output 存成計畫檔確認，再用 apply --plan ... --world-offline 還原。',
    },
  },
  {
    id: 'convert',
    group: 'translate',
    syntax: 'nbtcli convert <world> --lang <locale>',
    tier: 'preview',
    requiredArgs: {
      en: 'World folder and --lang <locale> (such as en_us or zh_tw)',
      'zh-tw': '世界資料夾與 --lang <語言代碼>（例如 en_us 或 zh_tw）',
    },
    summary: {
      en: 'Converts in-world text into translation keys and builds a matching language file in one go, without starting a paginated translation session.',
      'zh-tw': '不開翻譯進度，一次把地圖裡的文字全部抽成翻譯鍵並產出對應語言檔。',
    },
    resultAndNext: {
      en: 'Previews by default; save with --output or apply with --apply --world-offline.',
      'zh-tw': '預設僅預覽；可用 --output 存計畫，或加 --apply --world-offline 直接轉換。',
    },
  },
  {
    id: 'translate',
    group: 'translate',
    syntax: 'nbtcli translate <subcommand> <world> [options]',
    tier: 'preview',
    requiredArgs: {
      en: 'Subcommand (start, status, export, advance, submit, get, jump, note, glossary, reset, groups, memory, plan, export-lang, create-pack, seal, help)',
      'zh-tw': '子命令（start、status、export、advance、submit、get、jump、note、glossary、reset、groups、memory、plan、export-lang、create-pack、seal、help）',
    },
    summary: {
      en: 'Runs the full page-by-page adventure map translation workflow, glossary management, resource pack export, and completion sealing.',
      'zh-tw': '管理整套分頁式冒險地圖翻譯流程，包含進度追蹤、術語表、產生寫入計畫、匯出資源包與完成封存。',
    },
    resultAndNext: {
      en: 'All progress stays outside your world. Run nbtcli translate <subcommand> --help for options.',
      'zh-tw': '翻譯進度都存在世界資料夾外。執行 nbtcli translate <子命令> --help 可看各別參數。',
    },
  },
  {
    id: 'agent',
    group: 'agent',
    syntax: 'nbtcli agent <setup|submit|status|help> <world>',
    tier: 'preview',
    requiredArgs: {
      en: 'Subcommand (setup, submit, status, or help) and world folder',
      'zh-tw': '子命令（setup、submit、status 或 help）與世界資料夾',
    },
    summary: {
      en: 'Optionally saves default source/target languages and page-size preferences for external translation scripts or AI tools.',
      'zh-tw': '選用功能：幫這個世界記住預設的來源語言、目標語言與每頁筆數，方便外部翻譯腳本或 AI 工具沿用。',
    },
    resultAndNext: {
      en: 'Stored locally outside your world; never connects to the internet.',
      'zh-tw': '只存在本機狀態資料夾內，絕不會主動連網。',
    },
  },
];
