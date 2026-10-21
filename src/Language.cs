using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace TermLens {
public static class UiLanguage {
    public static string Current="en";
    public static string T(string english,string chinese) { return Current=="zh"?chinese:english; }
    public static string Mode(WorkMode mode) { return mode==WorkMode.Ask?T("Ask","提问"):mode==WorkMode.Polish?T("Polish","润色"):T("Explain","解释"); }
    static readonly Dictionary<string,string> chinese=new Dictionary<string,string> {
        {"TERMLENS  /  Settings & library","TERMLENS  /  设置与资料库"},
        {"Full Quote: include completed entries in this window","全 Quote：附加当前窗口内已完成的记录"},
        {"Ask sends your question and quoted entries. Full Quote adds completed text entries from this window.\nExplicit quotes take priority; you will be notified if long context is shortened.","Ask 发送当前问题及引用内容；全 Quote 会附加本窗口中已完成的文本记录。\n手动引用优先保留；上下文过长需要缩短时会提示。"},
        {"Read","查询"},{"Lookup prompt","解释提示词"},{"Connection","连接与语言"},{"Word history","查词历史"},{"English teacher","英语老师"},{"Voice","朗读"},{"Reading size","阅读外观"},{"Polish wording","润色提示词"},
        {"TERMLENS     /     Your reading companion\nF13  ·  Select a word or open the typing box","TERMLENS     /     阅读与表达助手\nF13  ·  查询选中文字，或打开输入框"},
        {"Selected text / 所选内容 — Chinese input is supported","查询内容（支持中文输入）"},
        {"Your instructions control the answer. Edit in English or Chinese.\nSelected text is sent separately; no special placeholder is needed.","用提示词控制解释风格，可用中文或英文编辑。\n所选文本单独发送，无需填写占位符。"},
        {"Include recent lookup terms / 附加近期查词","附加近期查词"},{"Recent terms","最近查词数量"},{"within hours","时间范围（小时）"},
        {"Save settings / 保存","保存设置"},{"Preview request","预览请求"},{"CS / Math default","恢复词典提示词"},{"Copy answer","复制回答"},
        {"API base URL","API 基础地址"},{"Model / 模型","模型"},{"API key / 密钥","API 密钥"},{"Show key / 显示密钥","显示密钥"},{"Max output tokens","最大输出 token"},{"Use Windows proxy / 使用系统代理","使用系统代理"},
        {"Interface language","界面语言"},{"Your key is protected for your Windows account.\nTest connection sends a short paid request.","密钥由当前 Windows 账户加密保存。\n测试连接会发送一次简短的 API 请求。"},{"Test connection","测试连接"},{"Careful answers (slower) / 更严谨的解释","启用深度思考（较慢）"},
        {"Ask uses your question and the quotes you explicitly add.\nOther records in the window are not sent automatically.","提问时会发送你的问题和手动添加的引用。\n窗口里的其他记录不会自动发送。"},{"Teacher default","恢复老师提示词"},
        {"Polish improves a sentence or search query without answering it.\nChoose a mode, then press Enter. Shift alone switches modes.","润色用于改写句子或搜索词，不回答其中的问题。\n选择模式后按 Enter 发送，单按 Shift 切换模式。"},{"Polish default","恢复润色提示词"},
        {"Saved words only / 只看生词","只看生词"},{"Open lookup","打开记录"},{"☆ Save / Unsave","☆ 保存 / 取消保存"},{"Export JSON","导出 JSON"},
        {"Text size · pt","字号（磅）"},{"Window width","窗口宽度"},{"Minimum height","最小高度"},{"Apply reading size","应用阅读外观"},{"Reset reading size","重置阅读外观"},
        {"Ctrl + mouse wheel adjusts text size. Your size is remembered.\nThe main window keeps records until you close it.\nF13 with no selection opens Explain. Last session restores your previous window.\n\nShift switches Explain / Ask / Polish. Shift+Enter inserts a new line.\nQuote adds a whole entry; select answer text and press F13 for the temporary dictionary.","Ctrl + 滚轮调整字号，关闭后仍记住大小。\n主窗口保留所有查询，直到你主动关闭。\n无选区按 F13 默认解释；「上次窗口」恢复上次记录。\n\n单按 Shift 切换解释 / 提问 / 润色，Shift+Enter 换行。\n引用按钮添加整条记录；在回答里选词后按 F13 打开临时词典。"},
        {"Start at sign-in","登录时启动"},{"Open TermLens","打开 TermLens"},{"Settings","设置"},{"Last session","上次窗口"},{"Exit","退出"},
        {"Explain / 解释","解释"},{"Stop / 停止","停止"},{"☆ Save word / 加入生词","☆ 加入生词"},
        {"Microsoft voice","微软语音"},{"Speed (%)","语速（%）"},{"Voice proxy","语音代理"},{"Test voice","测试朗读"},{"Stop audio","停止朗读"},
        {"Prepare audio after each search","每次查询后预加载语音"},{"Prepare","预加载内容"},{"Clear audio cache","清空语音缓存"},
        {"Scroll speed (%)","滚动速度（%）"},{"40% gives a gentler scroll; 100% follows the Windows wheel distance. Changes are saved immediately.","40% 滚动更缓，100% 使用 Windows 的滚动幅度。调整后立即保存。"},
        {"Keep reading window on top","阅读窗口置顶"},
        {"Background image","背景图片"},{"Background image (%)","背景图不透明度（%）"},{"Quick background image","快捷查询壁纸"},{"Quick background image (%)","快捷查询壁纸不透明度（%）"},{"Window opacity (%)","窗口透明度（%）"},
        {"Voice service","语音服务"},{"Read original text, answers, or your draft. A selection reads only those words.\nBaidu web voice connects directly without a key. Microsoft uses the voice and proxy above, or Windows offline voices when no online setup is present.\n0% is normal speed. Stop audio cancels preparation or playback.","可朗读原文、回答和草稿；有选区时只读选中的词。\n百度网页发音直连，无需密钥。微软使用上方的音色和代理；缺少在线配置时使用 Windows 离线语音。\n0% 为正常语速，「停止朗读」取消加载或播放。"},
        {"Read the original text, answer, or your unsent draft.\nWith a selection, Listen reads only the selected words.\nStop audio cancels preparation or playback.\n\nWindows voices work offline. A separately configured\nMicrosoft Translator service enables online voices\nand silent preloading. 0% is the normal reading speed.","可朗读原文、回答，以及未提交的输入。\n有选区时，只朗读选中的词。\n「停止朗读」可取消加载或播放。\n\n默认使用 Windows 离线语音。另行配置\nMicrosoft Translator 后支持在线音色\n和静默预加载。0% 为正常语速。"}
    };
    static string English(string original) {
        if(original=="Selected text / 所选内容 — Chinese input is supported") return "Selected text — Chinese input is supported";
        int slash=original.IndexOf(" / ",StringComparison.Ordinal);
        if(slash>=0 && original.Substring(slash+3).IndexOfAny(new[]{'解','阅','查','生','保','密','连','模','显','使','附','更','加','停'})>=0) return original.Substring(0,slash);
        return original;
    }
    public static string Translate(string original) {
        string translated;
        return Current=="zh" && chinese.TryGetValue(original,out translated)?translated:English(original);
    }
    public static void Collect(Control root,Dictionary<Control,string> labels) {
        // Never translate user input, model answers, prompts or stored history.
        if(root is Label || root is Button || root is CheckBox) labels[root]=root.Text;
        foreach(Control child in root.Controls) Collect(child,labels);
    }
    public static void Apply(Dictionary<Control,string> labels) { foreach(var item in labels) if(!item.Key.IsDisposed) item.Key.Text=Translate(item.Value); }
}
}
