import SwiftUI

/// One row in the drafts list: title plus message count and last-updated time.
struct DraftRow: View {
    let draft: DraftSummary

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(draft.title ?? "Untitled draft").font(.body)
            Text("\(draft.messageCount) message\(draft.messageCount == 1 ? "" : "s") · \(draft.updatedAt.formatted(date: .abbreviated, time: .shortened))")
                .font(.caption2).foregroundStyle(.secondary)
        }
    }
}
