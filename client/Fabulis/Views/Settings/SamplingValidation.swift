import Foundation

enum SamplingValidation {
    static func error(topP: String, maxTokens: String, minP: String, topK: String, topA: String) -> String? {
        for (name, raw) in [("Top P", topP), ("Min P", minP), ("Top A", topA)] {
            let text = raw.trimmingCharacters(in: .whitespacesAndNewlines)
            if !text.isEmpty {
                guard let value = Double(text), value.isFinite, (0...1).contains(value) else {
                    return "\(name) must be between 0 and 1, or empty to use the model default."
                }
            }
        }
        for (name, raw) in [("Maximum tokens", maxTokens), ("Top K", topK)] {
            let text = raw.trimmingCharacters(in: .whitespacesAndNewlines)
            if !text.isEmpty {
                guard let value = Int(text), (name == "Top K" ? value >= 0 : value > 0) else { return "\(name) must be a valid whole number, or empty to use the model default." }
            }
        }
        return nil
    }
}
