//
//  Item.swift
//  Fabulis
//
//  Created by Paul Vick on 9/2/25.
//

import Foundation
import SwiftData

@Model
final class Item {
    var timestamp: Date
    
    init(timestamp: Date) {
        self.timestamp = timestamp
    }
}
