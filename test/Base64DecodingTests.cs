namespace tests;
using System.Text;
//using SimdUnicode;
using System.Diagnostics;

public class Base64DecodingTests
{

    /**
Regular base64 decoding test

          {"Hello, World!", "SGVsbG8sIFdvcmxkIQ=="},
          {"GeeksforGeeks", "R2Vla3Nmb3JHZWVrcw=="},
          {"123456", "MTIzNDU2"},
          {"Base64 Encoding", "QmFzZTY0IEVuY29kaW5n"},
          {"!R~J2jL&mI]O)3=c:G3Mo)oqmJdxoprTZDyxEvU0MI.'Ww5H{G>}y;;+B8E_Ah,Ed[ PdBqY'^N>O$4:7LK1<:|7)btV@|{YWR$$Er59-XjVrFl4L}~yzTEd4'E[@k", "IVJ+SjJqTCZtSV1PKTM9YzpHM01vKW9xbUpkeG9wclRaRHl4RXZVME1JLidXdzVIe0c+fXk7OytCOEVfQWgsRWRbIFBkQnFZJ15OPk8kNDo3TEsxPDp8NylidFZAfHtZV1IkJEVyNTktWGpWckZsNEx9fnl6VEVkNCdFW0Br"}};

base64url decoding test


          {"Hello, World!", "SGVsbG8sIFdvcmxkIQ"},
          {"GeeksforGeeks", "R2Vla3Nmb3JHZWVrcw"},
          {"123456", "MTIzNDU2"},
          {"Base64 Encoding", "QmFzZTY0IEVuY29kaW5n"},
          {"!R~J2jL&mI]O)3=c:G3Mo)oqmJdxoprTZDyxEvU0MI.'Ww5H{G>}y;;+B8E_Ah,Ed[ PdBqY'^N>O$4:7LK1<:|7)btV@|{YWR$$Er59-XjVrFl4L}~yzTEd4'E[@k", "IVJ-SjJqTCZtSV1PKTM9YzpHM01vKW9xbUpkeG9wclRaRHl4RXZVME1JLidXdzVIe0c-fXk7OytCOEVfQWgsRWRbIFBkQnFZJ15OPk8kNDo3TEsxPDp8NylidFZAfHtZV1IkJEVyNTktWGpWckZsNEx9fnl6VEVkNCdFW0Br"}};

*/
}