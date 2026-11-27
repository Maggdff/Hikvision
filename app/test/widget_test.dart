import 'package:flutter_test/flutter_test.dart';
import 'package:app/core/models.dart';

void main() {
  test('compactNumber', () {
    expect(compactNumber(999), '999');
    expect(compactNumber(1500), '1.5K');
    expect(compactNumber(5600000000), '5.6B');
  });
  test('groupedNumber', () {
    expect(groupedNumber(5000), '5,000');
    expect(groupedNumber(10), '10');
    expect(groupedNumber(1234567), '1,234,567');
  });
  test('RoomState parses seats', () {
    final s = RoomState.fromJson({
      'id': 'a', 'title': 't', 'ownerUid': 'u', 'maxSeats': 9, 'myRole': 'host',
      'seats': [{'seat': 0, 'uid': 'u', 'role': 'host', 'points': 5}],
      'giftTotal': 5, 'createdAt': '2026-01-01T00:00:00Z',
    });
    expect(s.seatAt(0)!.points, 5);
    expect(s.seatAt(1), isNull);
  });
}
