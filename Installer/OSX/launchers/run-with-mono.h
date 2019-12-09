@import Foundation;

@interface RunWithMono : NSObject {
}

+ (int) runAssemblyWithMono:(NSString *)appName assembly:(NSString *)assembly major:(int) major minor:(int) minor;

@end