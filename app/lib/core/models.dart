class RoomSummary {
  final String id, title, ownerUid;
  final String? topic, country;
  final int seatedCount, maxSeats;
  final bool isPrivate;
  RoomSummary.fromJson(Map j)
      : id = j['id'] as String,
        title = j['title'] as String,
        ownerUid = j['ownerUid'] as String,
        topic = j['topic'] as String?,
        country = j['country'] as String?,
        seatedCount = (j['seatedCount'] ?? 0) as int,
        maxSeats = (j['maxSeats'] ?? 9) as int,
        isPrivate = (j['isPrivate'] ?? false) as bool;
}

class Seat {
  final int seat;
  final String uid, role;
  final int points;
  final String? frame;
  Seat.fromJson(Map j)
      : seat = j['seat'] as int,
        uid = j['uid'] as String,
        role = (j['role'] ?? 'speaker') as String,
        points = ((j['points'] ?? 0) as num).toInt(),
        frame = j['frame'] as String?;
}

class RoomState {
  final String id, title, ownerUid, myRole;
  final String? topic, ownerFrame;
  final int maxSeats, giftTotal;
  final int? mySeat;
  final List<Seat> seats;
  final DateTime? createdAt;
  RoomState.fromJson(Map j)
      : id = j['id'] as String,
        title = j['title'] as String,
        topic = j['topic'] as String?,
        ownerUid = j['ownerUid'] as String,
        maxSeats = (j['maxSeats'] ?? 9) as int,
        myRole = (j['myRole'] ?? 'listener') as String,
        mySeat = j['mySeat'] as int?,
        seats = ((j['seats'] ?? []) as List).map((e) => Seat.fromJson(e as Map)).toList(),
        ownerFrame = j['ownerFrame'] as String?,
        giftTotal = ((j['giftTotal'] ?? 0) as num).toInt(),
        createdAt = DateTime.tryParse((j['createdAt'] ?? '') as String);
  Seat? seatAt(int i) {
    for (final s in seats) {
      if (s.seat == i) return s;
    }
    return null;
  }
}

class JoinInfo {
  final String token, url, role;
  final int? seat;
  JoinInfo.fromJson(Map j)
      : token = j['token'] as String,
        url = j['url'] as String,
        role = (j['role'] ?? 'listener') as String,
        seat = j['seat'] as int?;
}

class Gift {
  final String id, nameAr, nameEn, tier;
  final int priceCoins;
  Gift.fromJson(Map j)
      : id = j['id'] as String,
        nameAr = j['nameAr'] as String,
        nameEn = (j['nameEn'] ?? '') as String,
        tier = (j['tier'] ?? 'small') as String,
        priceCoins = (j['priceCoins'] as num).toInt();
}

class ChatMessage {
  final int id;
  final String uid, body, kind;
  final Map? meta;
  ChatMessage.fromJson(Map j)
      : id = (j['id'] as num).toInt(),
        uid = j['uid'] as String,
        body = (j['body'] ?? '') as String,
        kind = (j['kind'] ?? 'text') as String,
        meta = j['meta'] as Map?;
}

class LevelInfo {
  final int points, level;
  LevelInfo.fromJson(Map j)
      : points = ((j['points'] ?? 0) as num).toInt(),
        level = ((j['level'] ?? 1) as num).toInt();
}

class PromoBanner {
  final String title;
  final String? subtitle;
  PromoBanner.fromJson(Map j)
      : title = j['title'] as String,
        subtitle = j['subtitle'] as String?;
}

String compactNumber(int n) {
  if (n >= 1000000000) return '${_trim(n / 1000000000)}B';
  if (n >= 1000000) return '${_trim(n / 1000000)}M';
  if (n >= 1000) return '${_trim(n / 1000)}K';
  return '$n';
}

String _trim(double v) {
  final s = v.toStringAsFixed(1);
  return s.endsWith('.0') ? s.substring(0, s.length - 2) : s;
}

String groupedNumber(int n) {
  final s = n.toString();
  final b = StringBuffer();
  for (var i = 0; i < s.length; i++) {
    if (i > 0 && (s.length - i) % 3 == 0) b.write(',');
    b.write(s[i]);
  }
  return b.toString();
}

class FrameItem {
  final String id, nameAr;
  final int priceCoins;
  final bool owned, equipped;
  FrameItem.fromJson(Map j)
      : id = j['id'] as String,
        nameAr = j['nameAr'] as String,
        priceCoins = (j['priceCoins'] as num).toInt(),
        owned = (j['owned'] ?? false) as bool,
        equipped = (j['equipped'] ?? false) as bool;
}
