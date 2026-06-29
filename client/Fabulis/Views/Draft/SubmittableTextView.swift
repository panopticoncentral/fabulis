import SwiftUI
import UIKit

/// A multiline prompt composer backed by UIKit so it can intercept the hardware
/// Return key on Mac Catalyst.
///
/// SwiftUI's `TextField` (and `.onKeyPress` / `.keyboardShortcut`) can't do
/// this: a focused multiline text field hands plain Return to its field editor,
/// which inserts a newline *before* any SwiftUI-level intercept runs. Two prior
/// attempts (`.onKeyPress(keys: [.return])`, then an invisible
/// `.keyboardShortcut(.return)` button) both lost that race on Catalyst.
///
/// The fix lives one layer down: a `UITextView` subclass exposes a Return
/// `UIKeyCommand` with `wantsPriorityOverSystemBehavior = true`, which pre-empts
/// the newline insert and fires `onReturn` instead. Shift+Return carries a
/// different modifier set, doesn't match the (empty-modifier) command, and so
/// falls through to the text view to insert a newline as expected.
struct PromptComposer: View {
    @Binding var text: String
    @Binding var isFocused: Bool
    /// False while a generation is streaming (and we're not editing): the field
    /// shows its text but rejects input, mirroring the old `.disabled(...)`.
    var isEditable: Bool
    /// Plain Return. The caller decides whether that means submit, save an edit,
    /// or nothing (empty / streaming).
    var onReturn: () -> Void
    /// True only while an edit is in progress. When false, Escape isn't
    /// intercepted (it falls through, matching the old `.onKeyPress(.escape)`
    /// returning `.ignored`).
    var handlesEscape: Bool
    /// Escape, invoked only when `handlesEscape` is true.
    var onEscape: () -> Void

    /// Driven by the text view's measured content height, capped at `maxLines`.
    @State private var height: CGFloat = SubmittableTextView.singleLineHeight

    var body: some View {
        SubmittableTextView(
            text: $text,
            isFocused: $isFocused,
            isEditable: isEditable,
            height: $height,
            onReturn: onReturn,
            handlesEscape: handlesEscape,
            onEscape: onEscape)
            .frame(height: height)
    }
}

struct SubmittableTextView: UIViewRepresentable {
    @Binding var text: String
    @Binding var isFocused: Bool
    var isEditable: Bool
    @Binding var height: CGFloat
    var onReturn: () -> Void
    var handlesEscape: Bool
    var onEscape: () -> Void

    static let maxLines = 5
    static let placeholder = "Prompt"
    private static let inset = UIEdgeInsets(top: 7, left: 5, bottom: 7, right: 5)
    private static let lineFragmentPadding: CGFloat = 5

    /// One line of body text plus vertical insets — the field's resting height.
    static var singleLineHeight: CGFloat {
        UIFont.preferredFont(forTextStyle: .body).lineHeight + inset.top + inset.bottom
    }

    private static func maxHeight(for font: UIFont) -> CGFloat {
        font.lineHeight * CGFloat(maxLines) + inset.top + inset.bottom
    }

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    func makeUIView(context: Context) -> ReturnInterceptingTextView {
        let view = ReturnInterceptingTextView()
        view.delegate = context.coordinator
        view.font = UIFont.preferredFont(forTextStyle: .body)
        view.adjustsFontForContentSizeCategory = true
        view.backgroundColor = .clear
        view.textContainerInset = Self.inset
        view.textContainer.lineFragmentPadding = Self.lineFragmentPadding
        // Scrolling stays ON so the text view never reports a content-sized
        // intrinsic height. With it off, a long prompt (e.g. when editing a
        // bubble) makes the view want to be enormous, which fights the
        // `.frame(height:)` clamp and breaks the layout. Instead we measure the
        // content height ourselves (see layoutSubviews) and clamp the frame to
        // at most `maxLines`; past that, the text view just scrolls internally.
        view.isScrollEnabled = true
        view.layer.cornerRadius = 6
        view.layer.borderWidth = 1
        view.setContentHuggingPriority(.defaultLow, for: .horizontal)

        // Placeholder, drawn inside the text container's content origin.
        let placeholder = UILabel()
        placeholder.text = Self.placeholder
        placeholder.font = view.font
        placeholder.textColor = .placeholderText
        placeholder.adjustsFontForContentSizeCategory = true
        placeholder.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(placeholder)
        NSLayoutConstraint.activate([
            placeholder.leadingAnchor.constraint(
                equalTo: view.leadingAnchor,
                constant: Self.inset.left + Self.lineFragmentPadding),
            placeholder.topAnchor.constraint(
                equalTo: view.topAnchor, constant: Self.inset.top),
        ])
        view.placeholderLabel = placeholder

        view.onReturn = onReturn
        view.onEscape = onEscape
        view.maxHeight = Self.maxHeight(for: view.font!)
        view.onHeightChange = { [weak coordinator = context.coordinator] newHeight in
            coordinator?.report(height: newHeight)
        }
        return view
    }

    func updateUIView(_ view: ReturnInterceptingTextView, context: Context) {
        context.coordinator.parent = self

        if view.text != text { view.text = text }
        view.isEditable = isEditable
        view.placeholderLabel?.isHidden = !text.isEmpty
        view.onReturn = onReturn
        view.onEscape = onEscape
        view.handlesEscape = handlesEscape
        view.maxHeight = Self.maxHeight(for: view.font ?? UIFont.preferredFont(forTextStyle: .body))
        view.layer.borderColor = UIColor.separator.resolvedColor(
            with: view.traitCollection).cgColor

        // Focus is requested programmatically (open draft, begin edit). Hop off
        // the SwiftUI update pass before touching the responder so the
        // delegate's focus write-back doesn't mutate state mid-update.
        if isFocused, !view.isFirstResponder, view.isEditable {
            DispatchQueue.main.async { view.becomeFirstResponder() }
        } else if !isFocused, view.isFirstResponder {
            DispatchQueue.main.async { view.resignFirstResponder() }
        }
    }

    final class Coordinator: NSObject, UITextViewDelegate {
        var parent: SubmittableTextView

        init(_ parent: SubmittableTextView) { self.parent = parent }

        func textViewDidChange(_ textView: UITextView) {
            parent.text = textView.text
            (textView as? ReturnInterceptingTextView)?.placeholderLabel?
                .isHidden = !textView.text.isEmpty
        }

        func textViewDidBeginEditing(_ textView: UITextView) {
            if !parent.isFocused { parent.isFocused = true }
        }

        func textViewDidEndEditing(_ textView: UITextView) {
            if parent.isFocused { parent.isFocused = false }
        }

        func report(height: CGFloat) {
            guard parent.height != height else { return }
            DispatchQueue.main.async { [weak self] in
                guard let self, self.parent.height != height else { return }
                self.parent.height = height
            }
        }
    }
}

/// `UITextView` that intercepts Return (no modifiers) and Escape via key
/// commands. The Return command sets `wantsPriorityOverSystemBehavior` so it
/// wins against the field editor's default newline insert on Mac Catalyst.
final class ReturnInterceptingTextView: UITextView {
    var onReturn: (() -> Void)?
    var onEscape: (() -> Void)?
    var onHeightChange: ((CGFloat) -> Void)?
    var handlesEscape = true
    var maxHeight: CGFloat = .greatestFiniteMagnitude
    weak var placeholderLabel: UILabel?

    override var keyCommands: [UIKeyCommand]? {
        let returnCommand = UIKeyCommand(
            input: "\r", modifierFlags: [], action: #selector(handleReturnCommand))
        returnCommand.wantsPriorityOverSystemBehavior = true
        var commands = [returnCommand]
        if handlesEscape {
            commands.append(UIKeyCommand(
                input: UIKeyCommand.inputEscape,
                modifierFlags: [],
                action: #selector(handleEscapeCommand)))
        }
        return commands
    }

    @objc private func handleReturnCommand() { onReturn?() }
    @objc private func handleEscapeCommand() { onEscape?() }

    override func layoutSubviews() {
        super.layoutSubviews()
        guard bounds.width > 0 else { return }
        // Full height the content wants at this width, clamped to maxLines.
        // sizeThatFits lays the text out independently of isScrollEnabled, so
        // this stays correct while the view scrolls past the cap.
        let fitted = sizeThatFits(
            CGSize(width: bounds.width, height: .greatestFiniteMagnitude)).height
        onHeightChange?(min(fitted, maxHeight))
    }
}
